using System.IO.Compression;
using System.Text;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;
using Sinan.Core.Models;
using Sinan.Core.Utils;
using Sinan.Data.Repositories;

namespace Sinan.Data.Excel;

/// <summary>
/// Excel 导出：标准版（内部周会）、原表版式（交回客户）、附加页（附件清单/变更记录/说明）。
///
/// 无损要点（docs/调研与方案.md 5.6）：
/// - 日期优先写 due_raw，"11.5" 不会变成 "2026-11-05"；
/// - `/` 原样导出，不能变成空白；
/// - 视觉合并：同机台连续时只在首行写机台号；
/// - 机台号按文本写入，不会变成科学计数法；
/// - 附件清单与变更记录放附加页，不破坏原表版式。
/// </summary>
public sealed class ExcelExportService
{
    private readonly Database _database;
    private readonly IssueRepository _issues;
    private readonly ProjectRepository _projects;
    private readonly AttachmentRepository _attachments;

    public ExcelExportService(Database database, IssueRepository issues,
        ProjectRepository projects, AttachmentRepository attachments)
    {
        _database = database;
        _issues = issues;
        _projects = projects;
        _attachments = attachments;
    }

    public ExportResult Export(ExportRequest request, User user)
    {
        var project = _projects.GetMain() ?? throw new InvalidOperationException("数据库尚未初始化项目。");
        var selected = Select(_issues.All(), request);
        var dir = Path.Combine(_database.Paths.ArchiveDir, "exports");
        Directory.CreateDirectory(dir);

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
        var baseName = (request.Layout ?? "original").ToLowerInvariant() switch
        {
            "standard" => $"{project.Code}_标准明细_{stamp}",
            "summary" => $"{project.Code}_统计汇总_{stamp}",
            _ => $"{project.Code}_原表版式_{stamp}"
        };

        // 界面指定了文件名就用它，但只取纯文件名：剥掉任何路径成分与非法字符，
        // 防止通过文件名把导出写到目录之外（导出目录是固定的 archive\exports）。
        var preferred = SanitizeFileName(request.PreferredFileName);
        if (preferred.Length > 0) baseName = preferred;

        // 同一分钟内连续导出（先原表版式看效果、再改范围重导）会撞名：
        // 直接 FileMode.Create 会把上一份悄悄覆盖，用户以为"两份都在"。
        // 这里在撞名时追加 _2 / _3，保证每次导出都留得下。
        var xlsxPath = Path.Combine(dir, baseName + ".xlsx");
        for (int n = 2; File.Exists(xlsxPath); n++)
            xlsxPath = Path.Combine(dir, $"{baseName}_{n}.xlsx");

        IWorkbook workbook = new XSSFWorkbook();
        var styles = new ExportStyles(workbook);
        var layout = (request.Layout ?? "original").ToLowerInvariant();
        switch (layout)
        {
            case "standard":
                WriteStandardSheet(workbook, styles, selected, user);
                break;
            case "summary":
                WriteSummarySheet(workbook, styles, selected);
                break;
            default:
                WriteOriginalSheet(workbook, styles, selected, user, request);
                break;
        }
        WriteAttachmentSheet(workbook, styles, selected);
        if (request.IncludeChangeLog) WriteChangeSheet(workbook, styles, selected);
        WriteInfoSheet(workbook, styles, project, request, user, selected);

        using (var stream = new FileStream(xlsxPath, FileMode.Create, FileAccess.Write))
            workbook.Write(stream);
        workbook.Close();

        var attachmentCount = selected.Sum(i => i.PhotoCount);
        var finalPath = xlsxPath;
        if (request.IncludeAttachments && attachmentCount > 0)
            finalPath = BuildZip(xlsxPath, selected, project);

        var file = new FileInfo(finalPath);
        return new ExportResult
        {
            OutputPath = file.FullName,
            IssueCount = selected.Count,
            AttachmentCount = attachmentCount,
            FileSize = file.Length
        };
    }

    /// <summary>
    /// 文件名净化：只取最后一段（剥掉路径），并替换 Windows 非法字符。
    /// 导出目录固定为 archive\exports，不允许借文件名把文件写到别处；
    /// 同时去掉 .xlsx 后缀，由调用方统一补，避免出现 "xxx.xlsx.xlsx"。
    /// </summary>
    private static string SanitizeFileName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        // 先剥路径（含 "..\.." 这类上跳），只留文件名部分。
        var name = raw.Trim();
        var slash = name.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0) name = name[(slash + 1)..];

        if (name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            name = name[..^5];
        else if (name.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        // 纯点号（"." / ".."）会变成当前目录/上级目录，必须挡掉。
        name = name.Trim().Trim('.');
        return name;
    }

    private static List<Issue> Select(List<Issue> all, ExportRequest request)
    {
        IEnumerable<Issue> query = all;
        if (!string.IsNullOrWhiteSpace(request.ProjectId))
            query = query.Where(i => i.ProjectId == request.ProjectId);
        if (!string.IsNullOrWhiteSpace(request.SubprojectId))
        {
            query = request.SubprojectId == ProjectRepository.UnassignedCode
                ? query.Where(i => i.SubprojectId == null)
                : query.Where(i => i.SubprojectId == request.SubprojectId);
        }
        if (!request.SelectAll && request.IssueIds.Count > 0)
            query = query.Where(i => request.IssueIds.Contains(i.Id));

        // 导出范围（设计稿 export 页「导出范围」四选一）。只按库内已有字段过滤，
        // 不在导出时重新算一套口径：倒挂/逾期都走 StageRules 同一判据。
        switch ((request.Scope ?? "all").ToLowerInvariant())
        {
            case "inverted":
                query = query.Where(i => i.IsInverted);
                break;
            case "overdue":
                query = query.Where(i => i.OverdueDays > 0);
                break;
            case "photos":
                query = query.Where(i => i.PhotoCount > 0);
                break;
        }

        return query.ToList();
    }

    // ---------------------------------------------------------------------
    // 标准版
    // ---------------------------------------------------------------------

    private static void WriteStandardSheet(IWorkbook workbook, ExportStyles styles,
        List<Issue> issues, User user)
    {
        var sheet = workbook.CreateSheet("问题台账");
        var headers = new[]
        {
            "序号", "机台号", "分项目", "问题描述", "解决措施",
            "方案确定-责任人", "方案确定-计划时间",
            "设计下图-责任人", "设计下图-计划时间",
            "物料准备-责任人", "物料准备-计划时间",
            "装配调试-责任人", "装配调试-计划时间",
            "完成情况", "状态", "优先级", "附件数", "逾期天数", "来源行"
        };
        WriteHeader(sheet, styles.Header, headers, 0);
        var widths = new[] { 7, 11, 11, 34, 34, 16, 16, 16, 16, 16, 16, 16, 16, 24, 10, 8, 8, 9, 8 };

        var rowIndex = 1;
        foreach (var issue in issues)
        {
            var row = sheet.CreateRow(rowIndex++);
            row.Height = 30 * 20;
            var values = new[]
            {
                issue.SeqNo,
                issue.MachineCode,
                issue.SubprojectCode ?? ProjectRepository.UnassignedCode,
                issue.Title,
                issue.Solution,
                issue.PlanStage?.OwnerText ?? "",
                DueOf(issue.PlanStage),
                issue.DesignStage?.OwnerText ?? "",
                DueOf(issue.DesignStage),
                issue.MaterialStage?.OwnerText ?? "",
                DueOf(issue.MaterialStage),
                issue.AssemblyStage?.OwnerText ?? "",
                DueOf(issue.AssemblyStage),
                issue.CompletionText,
                issue.Status,
                issue.Priority,
                issue.PhotoCount.ToString(),
                issue.OverdueDays.ToString(),
                issue.SourceRow?.ToString() ?? ""
            };
            for (var c = 0; c < values.Length; c++)
            {
                var cell = row.CreateCell(c);
                cell.SetCellValue(values[c]);
                cell.CellStyle = c is 3 or 4 or 13 ? styles.BodyWrap : styles.Body;
            }
        }

        for (var c = 0; c < widths.Length; c++) sheet.SetColumnWidth(c, widths[c] * 256);
        sheet.CreateFreezePane(4, 1);
        sheet.SetAutoFilter(new CellRangeAddress(0, 0, 0, headers.Length - 1));
    }

    // ---------------------------------------------------------------------
    // 统计汇总（设计稿 export 页「统计汇总」版式）
    // ---------------------------------------------------------------------

    /// <summary>
    /// 统计汇总表：只统计系统里已有的字段，不做任何推算。
    ///
    /// 【为什么单独一张表】原表版式与标准明细都是"一条问题一行"，开会时没人看 36 行明细；
    /// 汇总表把"谁的活最重 / 阶段填到哪一步 / 哪几条倒挂"三件事摊在一屏，用于协调会。
    /// 口径与桌面统计页完全一致：倒挂走 <see cref="StageRules.IsPlanInverted"/>，
    /// 逾期走 <see cref="StageRules.OverdueDays"/>，都只认语义日期。
    /// </summary>
    private static void WriteSummarySheet(IWorkbook workbook, ExportStyles styles, List<Issue> issues)
    {
        var sheet = workbook.CreateSheet("统计汇总");

        var rowIndex = 0;

        // —— 一、总览 ——
        var title = sheet.CreateRow(rowIndex++);
        title.CreateCell(0).SetCellValue("一、总览");
        title.GetCell(0).CellStyle = styles.GroupHeader;

        var overviewRows = new List<(string Key, string Value)>
        {
            ("问题总数", issues.Count.ToString()),
            ("机台数", issues.Select(i => i.MachineId).Distinct().Count().ToString()),
            ("分项目数", issues.Where(i => i.SubprojectId != null)
                .Select(i => i.SubprojectId).Distinct().Count().ToString()),
            ("计划倒挂", issues.Count(i => i.IsInverted).ToString()),
            ("已逾期", issues.Count(i => i.OverdueDays > 0).ToString()),
            ("已关闭", issues.Count(i => i.IsClosed).ToString()),
            ("照片/附件", issues.Sum(i => i.PhotoCount).ToString()),
            ("生成时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
        };
        foreach (var (key, value) in overviewRows)
        {
            var row = sheet.CreateRow(rowIndex++);
            var k = row.CreateCell(0);
            k.SetCellValue(key);
            k.CellStyle = styles.SubHeader;
            var v = row.CreateCell(1);
            v.SetCellValue(value);
            v.CellStyle = styles.Body;
        }

        // —— 二、责任人负载（按任务数降序，与统计页同口径）——
        rowIndex++;
        var loadTitle = sheet.CreateRow(rowIndex++);
        loadTitle.CreateCell(0).SetCellValue("二、责任人负载（四阶段任务数）");
        loadTitle.GetCell(0).CellStyle = styles.GroupHeader;

        // 每个"阶段 + 责任人"算一个任务；`/`（不适用）不计入。
        var tasks = new List<(string Owner, string Stage)>();
        foreach (var issue in issues)
        {
            foreach (var stage in issue.Stages)
            {
                if (stage.State == StageState.NotApplicable) continue;
                var owner = (stage.OwnerText ?? "").Trim();
                if (owner.Length == 0) continue;
                // 责任人原文可能是 "王工 / 冯工"，按 / 拆开后各算一个任务。
                foreach (var part in owner.Split('/', StringSplitOptions.RemoveEmptyEntries))
                {
                    var name = part.Trim();
                    if (name.Length > 0) tasks.Add((name, stage.Label));
                }
            }
        }

        WriteHeader(sheet, styles.Header, new[] { "责任人", "任务数", "占比", "涉及阶段" }, rowIndex++);
        var maxLoad = tasks.GroupBy(t => t.Owner).Select(g => g.Count()).DefaultIfEmpty(0).Max();
        foreach (var group in tasks.GroupBy(t => t.Owner).OrderByDescending(g => g.Count())
                     .ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            var row = sheet.CreateRow(rowIndex++);
            row.CreateCell(0).SetCellValue(group.Key);
            row.CreateCell(1).SetCellValue(group.Count());
            // 占比 = 本人任务数 / 最高负载（与统计页负载条同一换算，不是占全部任务的百分比）。
            row.CreateCell(2).SetCellValue(maxLoad == 0 ? "0%" : $"{group.Count() * 100d / maxLoad:0}%");
            var stages = string.Join("、", group.Select(t => t.Stage).Distinct());
            row.CreateCell(3).SetCellValue(stages);
            for (var c = 0; c < 4; c++) row.GetCell(c).CellStyle = styles.Body;
        }

        // —— 三、阶段填写率 ——
        rowIndex++;
        var fillTitle = sheet.CreateRow(rowIndex++);
        fillTitle.CreateCell(0).SetCellValue("三、阶段填写率");
        fillTitle.GetCell(0).CellStyle = styles.GroupHeader;

        WriteHeader(sheet, styles.Header,
            new[] { "阶段", "责任人已填", "计划已填", "已完成", "不适用", "合计" }, rowIndex++);
        foreach (var code in StageCodes.All)
        {
            var stages = issues.Select(i => i.Stage(code)).Where(s => s != null).Select(s => s!).ToList();
            var row = sheet.CreateRow(rowIndex++);
            row.CreateCell(0).SetCellValue(StageCodes.Label(code));
            row.CreateCell(1).SetCellValue(stages.Count(s => !string.IsNullOrWhiteSpace(s.OwnerText)));
            row.CreateCell(2).SetCellValue(stages.Count(s => !string.IsNullOrWhiteSpace(s.DueRaw)));
            row.CreateCell(3).SetCellValue(stages.Count(s => s.State == StageState.Done));
            row.CreateCell(4).SetCellValue(stages.Count(s => s.State == StageState.NotApplicable));
            row.CreateCell(5).SetCellValue(stages.Count);
            for (var c = 0; c < 6; c++) row.GetCell(c).CellStyle = styles.Body;
        }

        // —— 四、倒挂清单 ——
        rowIndex++;
        var invTitle = sheet.CreateRow(rowIndex++);
        invTitle.CreateCell(0).SetCellValue("四、计划倒挂清单（设计计划晚于装配计划）");
        invTitle.GetCell(0).CellStyle = styles.GroupHeader;

        WriteHeader(sheet, styles.Header,
            new[] { "序号", "机台号", "分项目", "问题", "设计计划", "装配计划", "相差天数" }, rowIndex++);
        foreach (var issue in issues.Where(i => i.IsInverted).OrderByDescending(i => StageRules.InvertedDays(i)))
        {
            var row = sheet.CreateRow(rowIndex++);
            row.CreateCell(0).SetCellValue(issue.SeqNo);
            row.CreateCell(1).SetCellValue(issue.MachineCode);
            row.CreateCell(2).SetCellValue(issue.SubprojectCode ?? ProjectRepository.UnassignedCode);
            row.CreateCell(3).SetCellValue(issue.Title);
            row.CreateCell(4).SetCellValue(issue.DesignDueRaw);
            row.CreateCell(5).SetCellValue(issue.AssemblyDueRaw);
            row.CreateCell(6).SetCellValue(StageRules.InvertedDays(issue));
            for (var c = 0; c < 7; c++) row.GetCell(c).CellStyle = styles.Body;
        }

        sheet.SetColumnWidth(0, 22 * 256);
        sheet.SetColumnWidth(1, 12 * 256);
        sheet.SetColumnWidth(2, 12 * 256);
        sheet.SetColumnWidth(3, 40 * 256);
        sheet.SetColumnWidth(4, 14 * 256);
        sheet.SetColumnWidth(5, 14 * 256);
        sheet.SetColumnWidth(6, 10 * 256);
    }

    // ---------------------------------------------------------------------
    // 原表版式
    // ---------------------------------------------------------------------

    private static void WriteOriginalSheet(IWorkbook workbook, ExportStyles styles,
        List<Issue> issues, User user, ExportRequest request)
    {
        var sheet = workbook.CreateSheet("问题清单");

        var groups = new[] { "序号", "机台号", "问题描述", "解决措施", "完成情况" };
        var stages = new[] { "方案确定", "设计下图", "物料准备", "装配调试" };
        var subs = new[] { "责任人", "计划时间" };

        var top = sheet.CreateRow(0);
        top.Height = 22 * 20;
        for (var c = 0; c < 5; c++)
        {
            var cell = top.CreateCell(c);
            cell.SetCellValue(groups[c]);
            cell.CellStyle = styles.GroupHeader;
        }
        for (var s = 0; s < stages.Length; s++)
        {
            var cell = top.CreateCell(5 + s * 2);
            cell.SetCellValue(stages[s]);
            cell.CellStyle = styles.GroupHeader;
            sheet.AddMergedRegion(new CellRangeAddress(0, 0, 5 + s * 2, 6 + s * 2));
        }

        var second = sheet.CreateRow(1);
        second.Height = 20 * 20;
        for (var s = 0; s < stages.Length; s++)
        {
            for (var k = 0; k < 2; k++)
            {
                var cell = second.CreateCell(5 + s * 2 + k);
                cell.SetCellValue(subs[k]);
                cell.CellStyle = styles.SubHeader;
            }
        }
        // 前 5 列两级纵向合并（序号/机台号/问题描述/解决措施/完成情况）。
        for (var c = 0; c < 5; c++)
            sheet.AddMergedRegion(new CellRangeAddress(0, 1, c, c));

        var note = sheet.CreateRow(2);
        note.Height = 18 * 20;
        var noteText = new[]
        {
            "原表序号，不重排", "格式异常保留原文", "问题点原文", "解决措施原文", "完成情况原文",
            "责任人原文", "计划原文", "责任人原文", "计划原文",
            "责任人原文", "计划原文", "责任人原文", "计划原文"
        };
        for (var c = 0; c < noteText.Length; c++)
        {
            var cell = note.CreateCell(c);
            cell.SetCellValue(noteText[c]);
            cell.CellStyle = styles.Note;
        }

        var rowIndex = 3;
        string? lastMachine = null;
        foreach (var issue in issues)
        {
            var row = sheet.CreateRow(rowIndex++);
            row.Height = 32 * 20;
            var machineCell = issue.MachineCode;
            var seqCell = issue.SeqNo;
            // 视觉合并：同机台连续只在首行写机台号/序号，其余留空，与原表一致。
            if (lastMachine == issue.MachineCode)
            {
                machineCell = "";
                seqCell = "";
            }
            lastMachine = issue.MachineCode;

            var values = new List<string>
            {
                seqCell, machineCell, issue.Title, issue.Solution, issue.CompletionText
            };
            foreach (var stage in new[]
                     {
                         issue.PlanStage, issue.DesignStage, issue.MaterialStage, issue.AssemblyStage
                     })
            {
                values.Add(stage?.OwnerText ?? "");
                values.Add(DueOf(stage));
            }

            for (var c = 0; c < values.Count; c++)
            {
                var cell = row.CreateCell(c);
                cell.SetCellValue(values[c]);
                cell.CellStyle = c is 2 or 3 or 4 ? styles.BodyWrap : styles.Body;
            }
        }

        var widths = new[] { 7, 11, 36, 36, 24, 14, 12, 14, 12, 14, 12, 14, 12 };
        for (var c = 0; c < widths.Length; c++) sheet.SetColumnWidth(c, widths[c] * 256);
        sheet.CreateFreezePane(3, 3);
    }

    // ---------------------------------------------------------------------
    // 附加页
    // ---------------------------------------------------------------------

    private void WriteAttachmentSheet(IWorkbook workbook, ExportStyles styles, List<Issue> issues)
    {
        var sheet = workbook.CreateSheet("附件清单");
        WriteHeader(sheet, styles.Header,
            new[] { "问题ID", "机台号", "问题描述", "文件名", "大小", "类型", "相对路径", "演示", "文件存在" }, 0);

        var rowIndex = 1;
        foreach (var issue in issues)
        {
            List<Attachment> list;
            if (issue.Attachments.Count > 0) list = issue.Attachments;
            else list = _attachments.List(issue.Id);
            foreach (var attachment in list)
            {
                var row = sheet.CreateRow(rowIndex++);
                var values = new[]
                {
                    issue.Id, issue.MachineCode, issue.Title, attachment.FileName,
                    attachment.SizeDisplay, attachment.Mime, attachment.StoredPath,
                    attachment.IsDemo ? "是" : "否",
                    attachment.IsDemo ? "演示占位" : (_attachments.FileExists(attachment) ? "是" : "否")
                };
                for (var c = 0; c < values.Length; c++)
                {
                    var cell = row.CreateCell(c);
                    cell.SetCellValue(values[c]);
                    cell.CellStyle = styles.Body;
                }
            }
        }

        var widths = new[] { 20, 11, 36, 26, 10, 16, 34, 8, 10 };
        for (var c = 0; c < widths.Length; c++) sheet.SetColumnWidth(c, widths[c] * 256);
        sheet.CreateFreezePane(0, 1);
    }

    private void WriteChangeSheet(IWorkbook workbook, ExportStyles styles, List<Issue> issues)
    {
        var sheet = workbook.CreateSheet("变更记录");
        WriteHeader(sheet, styles.Header,
            new[] { "时间", "操作人", "对象", "字段", "旧值", "新值" }, 0);

        var ids = issues.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        var entries = new List<(string Time, string User, string EntityId, string Field, string OldValue, string NewValue)>();
        using (var connection = _database.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT a.created_at, COALESCE(u.name, a.user_id, '') AS user_name,
                       a.entity_id, a.field, COALESCE(a.old_value,''), COALESCE(a.new_value,'')
                FROM activity_log a
                LEFT JOIN users u ON u.id = a.user_id
                WHERE a.entity='issue'
                ORDER BY a.id DESC LIMIT 500
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var entityId = reader.GetString(2);
                if (!ids.Contains(entityId)) continue;
                entries.Add((
                    reader.GetString(0), reader.GetString(1), entityId,
                    reader.GetString(3), reader.GetString(4), reader.GetString(5)));
            }
        }

        var rowIndex = 1;
        foreach (var entry in entries)
        {
            var row = sheet.CreateRow(rowIndex++);
            var when = AppTime.ParseIso(entry.Time);
            var values = new[]
            {
                when.HasValue ? when.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : entry.Time,
                entry.User, entry.EntityId, entry.Field, entry.OldValue, entry.NewValue
            };
            for (var c = 0; c < values.Length; c++)
            {
                var cell = row.CreateCell(c);
                cell.SetCellValue(values[c]);
                cell.CellStyle = styles.Body;
            }
        }

        var widths = new[] { 18, 12, 20, 18, 30, 30 };
        for (var c = 0; c < widths.Length; c++) sheet.SetColumnWidth(c, widths[c] * 256);
        sheet.CreateFreezePane(0, 1);
    }

    private static void WriteInfoSheet(IWorkbook workbook, ExportStyles styles,
        Project project, ExportRequest request, User user, List<Issue> issues)
    {
        var sheet = workbook.CreateSheet("导出说明");
        var rows = new List<(string Key, string Value)>
        {
            ("项目", $"{project.Code} {project.Name}"),
            ("导出时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
            ("导出人", $"{user.Name}（{user.EmployeeNo}）"),
            ("导出模板", request.LayoutLabel),
            ("问题条数", issues.Count.ToString()),
            ("范围", request.SubprojectId ?? "全部"),
            ("照片/附件数", issues.Sum(i => i.PhotoCount).ToString()),
            ("数据来源", "本机司南数据库；演示附件标记 is_demo=1，不包含真实现场照片"),
            ("字段口径", "日期优先导出原文（due_raw）；`/` 原样保留；序号不重排；来源行可追溯")
        };

        var title = sheet.CreateRow(0);
        title.CreateCell(0).SetCellValue("导出说明");
        title.GetCell(0).CellStyle = styles.GroupHeader;

        var rowIndex = 1;
        foreach (var (key, value) in rows)
        {
            var row = sheet.CreateRow(rowIndex++);
            var keyCell = row.CreateCell(0);
            keyCell.SetCellValue(key);
            keyCell.CellStyle = styles.SubHeader;
            var valueCell = row.CreateCell(1);
            valueCell.SetCellValue(value);
            valueCell.CellStyle = styles.BodyWrap;
        }
        sheet.SetColumnWidth(0, 16 * 256);
        sheet.SetColumnWidth(1, 70 * 256);
    }

    // ---------------------------------------------------------------------
    // 附件 ZIP
    // ---------------------------------------------------------------------

    private string BuildZip(string xlsxPath, List<Issue> issues, Project project)
    {
        var zipPath = Path.ChangeExtension(xlsxPath, ".zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);

        var manifest = new StringBuilder();
        manifest.AppendLine("issue_id,machine,title,file_name,stored_path,size,mime,is_demo,file_exists");
        var attached = 0;

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(xlsxPath, Path.GetFileName(xlsxPath));

            foreach (var issue in issues)
            {
                foreach (var attachment in _attachments.List(issue.Id))
                {
                    var exists = _attachments.FileExists(attachment);
                    manifest.AppendLine(string.Join(",",
                        Csv(issue.Id), Csv(issue.MachineCode), Csv(issue.Title),
                        Csv(attachment.FileName), Csv(attachment.StoredPath),
                        attachment.Size.ToString(), Csv(attachment.Mime),
                        attachment.IsDemo ? "1" : "0", exists ? "1" : "0"));

                    if (!exists) continue;
                    var source = _attachments.AbsolutePath(attachment.StoredPath);
                    var entryName = $"attachments/{issue.Id}/{Path.GetFileName(source)}";
                    zip.CreateEntryFromFile(source, entryName);
                    attached++;
                }
            }

            // 注意：StreamWriter 必须在创建下一个 ZIP 条目之前 Dispose。
            // 用 `using var` 会活到方法结尾，导致 CreateEntry("manifest.csv") 抛
            // “Entries cannot be created while previously created entries are still open.”
            var readmeEntry = zip.CreateEntry("attachments/README.txt");
            using (var writer = new StreamWriter(readmeEntry.Open(), new UTF8Encoding(false)))
            {
                writer.WriteLine($"导出时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine($"项目：{project.Code} {project.Name}");
                writer.WriteLine($"真实附件 {attached} 个；is_demo=1 的演示占位照片不包含文件。");
                writer.WriteLine("演示附件仅用于界面演示，不得当作现场照片使用。");
            }

            var csvEntry = zip.CreateEntry("manifest.csv");
            using (var csvWriter = new StreamWriter(csvEntry.Open(), new UTF8Encoding(true)))
            {
                csvWriter.Write(manifest.ToString());
            }
        }

        // 单独保留 xlsx 便于直接打开；ZIP 作为完整交付包。
        return zipPath;
    }

    // ---------------------------------------------------------------------
    // 样式与工具
    // ---------------------------------------------------------------------

    private static void WriteHeader(ISheet sheet, ICellStyle style, string[] headers, int rowIndex)
    {
        var row = sheet.CreateRow(rowIndex);
        row.Height = 24 * 20;
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = row.CreateCell(c);
            cell.SetCellValue(headers[c]);
            cell.CellStyle = style;
        }
    }

    /// <summary>导出优先原文；原文为空才格式化语义日期，保证 11.5 不会变成 2026-11-05。</summary>
    private static string DueOf(IssueStage? stage)
    {
        if (stage == null) return "";
        if (!string.IsNullOrWhiteSpace(stage.DueRaw)) return stage.DueRaw;
        return stage.DueDate.HasValue ? RawDateParser.FormatRaw(stage.DueDate) : "";
    }

    private static string Csv(string? value)
    {
        var text = value ?? "";
        return text.Contains(',') || text.Contains('"') || text.Contains('\n')
            ? "\"" + text.Replace("\"", "\"\"") + "\""
            : text;
    }

    private sealed class ExportStyles
    {
        public ICellStyle GroupHeader { get; }
        public ICellStyle SubHeader { get; }
        public ICellStyle Header { get; }
        public ICellStyle Body { get; }
        public ICellStyle BodyWrap { get; }
        public ICellStyle Note { get; }

        public ExportStyles(IWorkbook workbook)
        {
            var headerFont = workbook.CreateFont();
            headerFont.IsBold = true;
            headerFont.FontHeightInPoints = 10;
            headerFont.FontName = "微软雅黑";

            var bodyFont = workbook.CreateFont();
            bodyFont.FontHeightInPoints = 10;
            bodyFont.FontName = "微软雅黑";

            GroupHeader = workbook.CreateCellStyle();
            GroupHeader.SetFont(headerFont);
            GroupHeader.Alignment = HorizontalAlignment.Center;
            GroupHeader.VerticalAlignment = VerticalAlignment.Center;
            GroupHeader.FillForegroundColor = IndexedColors.LightYellow.Index;
            GroupHeader.FillPattern = FillPattern.SolidForeground;
            ApplyBorder(GroupHeader);

            SubHeader = workbook.CreateCellStyle();
            SubHeader.CloneStyleFrom(GroupHeader);
            SubHeader.FillForegroundColor = IndexedColors.LightOrange.Index;

            Header = workbook.CreateCellStyle();
            Header.CloneStyleFrom(GroupHeader);

            Body = workbook.CreateCellStyle();
            Body.SetFont(bodyFont);
            Body.VerticalAlignment = VerticalAlignment.Center;
            Body.BorderBottom = BorderStyle.Hair;
            Body.BorderTop = BorderStyle.Hair;
            Body.BorderLeft = BorderStyle.Hair;
            Body.BorderRight = BorderStyle.Hair;

            BodyWrap = workbook.CreateCellStyle();
            BodyWrap.CloneStyleFrom(Body);
            BodyWrap.WrapText = true;
            BodyWrap.VerticalAlignment = VerticalAlignment.Top;

            Note = workbook.CreateCellStyle();
            Note.SetFont(bodyFont);
            Note.FillForegroundColor = IndexedColors.Grey25Percent.Index;
            Note.FillPattern = FillPattern.SolidForeground;
            Note.WrapText = true;
            ApplyBorder(Note);
        }

        private static void ApplyBorder(ICellStyle style)
        {
            style.BorderBottom = BorderStyle.Thin;
            style.BorderTop = BorderStyle.Thin;
            style.BorderLeft = BorderStyle.Thin;
            style.BorderRight = BorderStyle.Thin;
        }
    }
}
