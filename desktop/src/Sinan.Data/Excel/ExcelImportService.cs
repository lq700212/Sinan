using System.Globalization;
using System.Security.Cryptography;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using Sinan.Core.Models;
using Sinan.Core.Utils;
using Sinan.Data.Repositories;

namespace Sinan.Data.Excel;

/// <summary>
/// Excel 导入：表头自适应探测 → 列映射 → 校验 → 与库内比对 → 预览/写入。
///
/// 设计依据（docs/调研与方案.md 5.3）：
/// - 原表不是固定格式，表头行靠"机台/设备 + 问题描述"关键字定位，不写死行号；
/// - 两级表头：上行是阶段名（方案确定/设计下图/物料准备/装配调试），下行是责任人/计划时间，
///   合并单元格按区域首格取值；
/// - 视觉合并：序号/机台号只在分组首行写，向下填充但不改 source_row；
/// - 双字段：日期原文原样进 due_raw，能解析才写 due_date；
/// - 业务主键：机台号 + 问题描述；种子/导入来源可覆盖，现场人工改过的进"冲突"不静默覆盖。
/// </summary>
public sealed class ExcelImportService
{
    private const int HeaderScanRows = 20;
    private const long MaxFileSize = 20 * 1024 * 1024;

    private static readonly string[] StageKeywords = { "方案", "设计", "物料", "备料", "装配", "调试" };

    private readonly Database _database;
    private readonly IssueRepository _issues;
    private readonly ProjectRepository _projects;

    public ExcelImportService(Database database, IssueRepository issues, ProjectRepository projects)
    {
        _database = database;
        _issues = issues;
        _projects = projects;
    }

    /// <summary>只读解析与比对，不写库。界面先展示预览，用户确认后才 Apply。</summary>
    public ImportPreview Preview(string filePath)
    {
        var file = new FileInfo(filePath);
        if (!file.Exists) throw new FileNotFoundException("找不到 Excel 文件。", filePath);
        if (file.Length > MaxFileSize)
            throw new InvalidOperationException($"文件超过 {MaxFileSize / 1024 / 1024} MB 限制。");

        var project = _projects.GetMain() ?? throw new InvalidOperationException("数据库尚未初始化项目。");
        var hash = HashOf(filePath);
        var year = YearFromName(file.Name);

        IWorkbook workbook;
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            workbook = WorkbookFactory.Create(stream);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException("无法读取 Excel，请确认文件未加密且格式为 .xls/.xlsx。", ex);
        }

        var sheet = PickSheet(workbook);
        var map = DetectHeader(sheet)
            ?? throw new InvalidOperationException("未找到表头：表头行需要同时包含“机台号/设备号”和“问题描述/存在问题”。");

        var preview = new ImportPreview
        {
            FilePath = file.FullName,
            FileName = file.Name,
            FileHash = hash,
            ProjectCode = project.Code,
            HeaderRow = map.TitleRow + 1,
            // 表头布局：有三行表头（阶段组 + 子表头 + 标题）就说明是原表版式，
            // 否则按扁平表头处理。第 2 步列映射页直接展示，用户能对上原表。
            HeaderLayout = map.GroupRow >= 0 ? "三行表头（阶段组 + 子表头）" : "扁平表头",
            ImportedAt = DateTime.Now
        };

        preview.Messages.Add($"表头识别：第 {map.TitleRow + 1} 行；数据从第 {map.DataStartRow + 1} 行开始。");
        if (map.GroupRow >= 0)
            preview.Messages.Add(
                $"阶段列组：第 {map.GroupRow + 1} 行；子表头：第 {(map.SubRow >= 0 ? map.SubRow + 1 : map.TitleRow + 1)} 行。");
        if (map.Machine < 0 || map.Title < 0)
            throw new InvalidOperationException("列映射失败：缺少机台号或问题描述列。");
        if (map.Owner.All(c => c < 0) && map.Due.All(c => c < 0))
            preview.Messages.Add("警告：未识别到任何阶段列（责任人/计划时间），只导入基础字段。");

        var existing = _issues.All()
            .GroupBy(i => Key(i.MachineCode, i.Title), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var lastMachine = "";
        var lastSeq = "";
        var seqNumbers = new List<int>();
        var invertedRows = 0;
        var textPlanRows = 0;
        var machineWarnRows = 0;

        for (var rowIndex = map.DataStartRow; rowIndex <= sheet.LastRowNum; rowIndex++)
        {
            var row = sheet.GetRow(rowIndex);
            if (row == null) continue;

            var machineRaw = CellText(row.GetCell(map.Machine));
            var title = CellText(row.GetCell(map.Title));
            var seqRaw = map.Seq >= 0 ? CellText(row.GetCell(map.Seq)) : "";

            // 视觉合并：只有分组首行有机台号/序号，其余行沿用上一行。
            if (machineRaw.Length > 0) lastMachine = machineRaw;
            if (seqRaw.Length > 0) lastSeq = seqRaw;
            var machineCode = TextUtil.Normalize(lastMachine.Length > 0 ? lastMachine : machineRaw);

            if (machineCode.Length == 0 && title.Length == 0) continue;

            preview.SourceRowCount++;
            var item = new ImportRowPreview
            {
                SourceRow = rowIndex + 1,
                MachineCode = machineCode,
                SeqNo = seqRaw.Length > 0 ? seqRaw : lastSeq,
                Title = title
            };

            var warnings = item.Warnings;
            if (machineCode.Length == 0)
                warnings.Add("机台号为空，该行不导入。");
            else if (!TextUtil.LooksLikeMachineCode(machineCode))
            {
                warnings.Add($"机台号“{machineCode}”不像标准机台号，已允许导入但需人工确认。");
                machineWarnRows++;
            }
            else if (!TextUtil.IsStrictMachineCode(machineCode))
            {
                warnings.Add($"机台号“{machineCode}”位数异常（如多一位），已归入待确认，请核对原表。");
                machineWarnRows++;
            }

            if (title.Length == 0)
                warnings.Add("问题描述为空，该行不导入。");

            var draft = new IssueDraft
            {
                ProjectId = project.Id,
                MachineCode = machineCode,
                SeqNo = item.SeqNo,
                Title = title,
                Solution = map.Solution >= 0 ? CellText(row.GetCell(map.Solution)) : "",
                CompletionText = map.Completion >= 0 ? CellText(row.GetCell(map.Completion)) : "",
                SourceRow = item.SourceRow
            };

            var stageDrafts = new[] { draft.Plan, draft.Design, draft.Material, draft.Assembly };
            for (var s = 0; s < 4; s++)
            {
                var stage = stageDrafts[s];
                stage.Code = StageCodes.All[s];
                stage.OwnerText = map.Owner[s] >= 0 ? CellText(row.GetCell(map.Owner[s])) : "";
                stage.DueRaw = map.Due[s] >= 0 ? CellText(row.GetCell(map.Due[s])) : "";
                stage.State = StageRules.InferState(stage.OwnerText, stage.DueRaw);
            }

            // 日期原文无法解析时只给警告，原文照存（`已下图`、`/`、空白不在此列）。
            for (var s = 0; s < 4; s++)
            {
                var stage = stageDrafts[s];
                if (string.IsNullOrWhiteSpace(stage.DueRaw)) continue;
                if (RawDateParser.IsNotApplicable(stage.DueRaw)) continue;
                if (RawDateParser.MeansDrawingIssued(stage.DueRaw))
                {
                    // "已下图"这类文本计划：不是日期也不是"/"，原文必须原样保留，
                    // 但不能静默当成正常日期——导入确认弹窗单独计数，让人看见。
                    textPlanRows++;
                    continue;
                }
                if (!RawDateParser.Parse(stage.DueRaw, year).HasValue)
                {
                    warnings.Add($"{stage.Label}计划“{stage.DueRaw}”无法解析为日期，按原文保留。");
                    textPlanRows++;
                }
            }

            if (StageRules.IsPlanInverted(
                    RawDateParser.Parse(draft.Design.DueRaw, year),
                    RawDateParser.Parse(draft.Assembly.DueRaw, year)))
            {
                warnings.Add("设计下图计划晚于装配调试计划（计划倒挂）。");
                invertedRows++;
            }

            if (!string.IsNullOrWhiteSpace(draft.SeqNo) &&
                int.TryParse(draft.SeqNo, out var seqValue) && seqValue > 0)
                seqNumbers.Add(seqValue);

            item.DesignOwner = draft.Design.OwnerText;
            item.AssemblyOwner = draft.Assembly.OwnerText;
            item.DesignDue = draft.Design.DueRaw;
            item.AssemblyDue = draft.Assembly.DueRaw;
            item.Draft = draft;

            if (machineCode.Length == 0 || title.Length == 0)
            {
                item.Kind = ImportRowKind.Error;
                preview.ErrorCount++;
            }
            else if (!existing.TryGetValue(Key(machineCode, title), out var same))
            {
                item.Kind = ImportRowKind.Insert;
                preview.InsertCount++;
            }
            else if (SameContent(same, draft, year))
            {
                item.Kind = ImportRowKind.Unchanged;
                preview.UnchangedCount++;
            }
            else if (IsImportedSource(same))
            {
                item.Kind = ImportRowKind.Update;
                preview.UpdateCount++;
                draft.Id = same.Id;
            }
            else
            {
                item.Kind = ImportRowKind.Conflict;
                preview.ConflictCount++;
                draft.Id = same.Id;
                item.ExistingIssueId = same.Id;
                warnings.Add("系统内已人工修改过，Excel 也有变化；默认跳过，选“全量覆盖”才会写入。");
                // 逐条决策需要"系统值 vs 原表值"看得见，否则用户只能盲选。
                var (systemText, excelText) = DiffSummary(same, draft, year);
                item.ConflictSystemText = systemText;
                item.ConflictExcelText = excelText;
            }

            if (warnings.Count > 0) preview.WarningCount += warnings.Count;
            preview.Rows.Add(item);
        }

        preview.Total = preview.Rows.Count;
        preview.InvertedRowCount = invertedRows;
        preview.TextPlanRowCount = textPlanRows;
        preview.MachineWarningCount = machineWarnRows;

        var gaps = FindSeqGaps(seqNumbers);
        if (gaps.Count > 0)
            preview.Messages.Add($"序号缺口：{string.Join(" / ", gaps)}（保留原序号，不重排）。");
        if (invertedRows > 0)
            preview.Messages.Add($"计划倒挂：{invertedRows} 行。");

        using (var connection = _database.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT created_at FROM import_batches
                WHERE file_hash=$hash ORDER BY created_at DESC LIMIT 1
                """;
            command.Parameters.AddWithValue("$hash", hash);
            if (command.ExecuteScalar() is string previous && previous.Length > 0)
            {
                var when = AppTime.ParseIso(previous);
                preview.Messages.Add(when.HasValue
                    ? $"该文件已于 {when.Value.ToLocalTime():MM-dd HH:mm} 导入过，请确认是否需要重复导入。"
                    : "该文件曾导入过，请确认是否需要重复导入。");
            }
        }

        workbook.Close();
        return preview;
    }

    /// <summary>
    /// 按预览结果写库。strategy：
    /// - only_new：只新增；
    /// - merge（推荐）：新增 + 覆盖导入来源的更新；冲突行按每行的
    ///   <see cref="ImportRowPreview.TakeExcelOnConflict"/> 逐条决定（用户在导入确认弹窗里勾的）；
    /// - overwrite：新增 + 更新 + 冲突强制覆盖（Excel 视为唯一真源）。
    /// 每行独立事务，单行失败不影响其他行，失败原因写进 Messages。
    /// </summary>
    public ImportResult Apply(ImportPreview preview, string strategy, User user)
    {
        var project = _projects.GetMain() ?? throw new InvalidOperationException("数据库尚未初始化项目。");
        var batchId = IdGen.NewBatchId();
        var now = AppTime.UtcNowIso();

        var result = new ImportResult
        {
            BatchId = batchId,
            Total = preview.Rows.Count,
            Warnings = preview.WarningCount
        };

        foreach (var row in preview.Rows)
        {
            if (row.Kind == ImportRowKind.Error || row.Draft == null)
            {
                result.Skipped++;
                continue;
            }

            if (row.Kind == ImportRowKind.Unchanged)
            {
                result.Unchanged++;
                continue;
            }

            // 冲突行的处置优先级：
            //   1) 用户在确认弹窗里明确选了"保留系统值" → 无论什么策略都跳过（人的决定高于策略）；
            //   2) only_new → 跳过（策略本身就不碰已有行）；
            //   3) overwrite → 写入；
            //   4) merge → 看该行是否被勾成"用原表覆盖"（默认勾选）。
            if (row.Kind == ImportRowKind.Conflict)
            {
                if (!row.TakeExcelOnConflict || strategy == "only_new")
                {
                    result.Conflicts++;
                    result.Skipped++;
                    continue;
                }
            }

            if (row.Kind == ImportRowKind.Update && strategy == "only_new")
            {
                result.Skipped++;
                continue;
            }

            try
            {
                var draft = row.Draft;
                draft.ProjectId = project.Id;
                draft.SourceBatchId = batchId;
                draft.SourceRow = row.SourceRow;

                if (row.Kind == ImportRowKind.Insert)
                {
                    var machine = _projects.GetOrCreateMachine(project.Id, draft.MachineCode, now);
                    draft.MachineId = machine.Id;
                    draft.SubprojectId = machine.SubprojectId;
                    draft.Status = StageRules.DeriveStatus(BuildStages(draft), draft.CompletionText);
                    _issues.Create(draft, user);
                    result.Inserted++;
                }
                else
                {
                    _issues.ApplyImported(draft, user);
                    result.Updated++;
                }
            }
            catch (Exception ex)
            {
                result.Skipped++;
                result.Messages.Add($"第 {row.SourceRow} 行写入失败：{ex.Message}");
            }
        }

        ArchiveOriginal(preview, batchId);

        using (var connection = _database.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO import_batches
                    (id, project_id, file_name, file_hash, strategy, total, inserted, updated,
                     skipped, warnings, created_by, created_at)
                VALUES
                    ($id, $project, $name, $hash, $strategy, $total, $inserted, $updated,
                     $skipped, $warnings, $user, $now)
                """;
            command.Parameters.AddWithValue("$id", batchId);
            command.Parameters.AddWithValue("$project", project.Id);
            command.Parameters.AddWithValue("$name", preview.FileName);
            command.Parameters.AddWithValue("$hash", preview.FileHash);
            command.Parameters.AddWithValue("$strategy", strategy);
            command.Parameters.AddWithValue("$total", result.Total);
            command.Parameters.AddWithValue("$inserted", result.Inserted);
            command.Parameters.AddWithValue("$updated", result.Updated);
            command.Parameters.AddWithValue("$skipped", result.Skipped);
            command.Parameters.AddWithValue("$warnings", result.Warnings);
            command.Parameters.AddWithValue("$user", user.Id);
            command.Parameters.AddWithValue("$now", now);
            command.ExecuteNonQuery();
        }

        result.Messages.Insert(0, $"导入批次 {batchId} 已生成，原文件已归档。");
        return result;
    }

    private void ArchiveOriginal(ImportPreview preview, string batchId)
    {
        var dir = Path.Combine(_database.Paths.ArchiveDir, "imports");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, $"{batchId}_{preview.FileName}");
        try
        {
            File.Copy(preview.FilePath, target, overwrite: true);
        }
        catch (IOException)
        {
            // 归档失败不阻断导入，报告里仍有原始文件路径可供人工处理。
        }
    }

    private static List<IssueStage> BuildStages(IssueDraft draft) => new()
    {
        new IssueStage
        {
            Code = StageCodes.Plan, OwnerText = draft.Plan.OwnerText,
            DueRaw = draft.Plan.DueRaw, State = draft.Plan.State
        },
        new IssueStage
        {
            Code = StageCodes.Design, OwnerText = draft.Design.OwnerText,
            DueRaw = draft.Design.DueRaw, State = draft.Design.State
        },
        new IssueStage
        {
            Code = StageCodes.Material, OwnerText = draft.Material.OwnerText,
            DueRaw = draft.Material.DueRaw, State = draft.Material.State
        },
        new IssueStage
        {
            Code = StageCodes.Assembly, OwnerText = draft.Assembly.OwnerText,
            DueRaw = draft.Assembly.DueRaw, State = draft.Assembly.State
        }
    };

    /// <summary>
    /// 冲突行的"系统值 vs 原表值"摘要：逐阶段比对，把同一阶段的责任人与计划合成一句。
    ///
    /// 【为什么做成摘要而不是整行 diff】设计稿 dlg-import-conflict 的冲突行只有一行高度，
    /// 展示 "装配计划 11-15 → 原表 11-01" 这种粒度。完整字段差异放在预览表里看，
    /// 这里只回答"这一条到底要改什么"，否则用户面对一屏字段无从判断该覆盖还是保留。
    ///
    /// 【为什么按阶段归并】第一版逐字段追加，出现"方案确定计划 12.2 / 方案确定责任人 朱工
    /// 等 8 项"这种同阶段拆成两条、还被截断成"等 8 项"的读法——用户根本判断不了。
    /// 按阶段合成"方案确定（朱工 12.2）"才是一句人话。
    /// </summary>
    private static (string SystemText, string ExcelText) DiffSummary(Issue issue, IssueDraft draft, int year)
    {
        var system = new List<string>();
        var excel = new List<string>();

        foreach (var code in StageCodes.All)
        {
            var existing = issue.Stage(code);
            var incoming = code switch
            {
                StageCodes.Plan => draft.Plan,
                StageCodes.Design => draft.Design,
                StageCodes.Material => draft.Material,
                _ => draft.Assembly
            };
            var label = StageCodes.Label(code);

            // existing 为 null（历史数据缺阶段行）时按"有差异"处理：这是需要人看见的异常，
            // 不能因为拿不到系统值就静默认为相同。
            var dueChanged = existing == null || !SameDue(existing, incoming.DueRaw, year);
            var ownerChanged = existing == null ||
                !string.Equals(existing.OwnerText, incoming.OwnerText, StringComparison.Ordinal);
            if (!dueChanged && !ownerChanged) continue;

            // 同一阶段内"责任人 + 计划"合成一个括号，只列真正变了的那几项。
            system.Add($"{label}（{Describe(existing?.OwnerText, existing?.DueRaw, ownerChanged, dueChanged)}）");
            excel.Add($"{label}（{Describe(incoming.OwnerText, incoming.DueRaw, ownerChanged, dueChanged)}）");
        }

        if (!string.Equals(issue.Solution, draft.Solution, StringComparison.Ordinal))
        {
            system.Add("解决措施");
            excel.Add("解决措施");
        }

        // 差异阶段太多时只给前两项 + 条数；用户想看细节去预览表逐行看。
        static string Join(List<string> parts) => parts.Count switch
        {
            0 => "无差异",
            <= 2 => string.Join(" / ", parts),
            _ => $"{string.Join(" / ", parts.Take(2))} 等 {parts.Count} 项"
        };

        return (Join(system), Join(excel));

        // 只把变化的那几项写进括号，没变的省略，避免"（朱工 12.2）"这种看着像全变了。
        static string Describe(string? owner, string? due, bool ownerChanged, bool dueChanged)
        {
            var parts = new List<string>();
            if (ownerChanged)
            {
                var o = (owner ?? "").Trim();
                parts.Add(o.Length == 0 ? "无责任人" : o);
            }
            if (dueChanged)
            {
                var d = (due ?? "").Trim();
                parts.Add(d.Length == 0 ? "无计划" : d);
            }
            return string.Join(" ", parts);
        }
    }

    /// <summary>只有导入/种子来源的记录允许被 Excel 直接更新；系统内新建或人工改过的一律进冲突。</summary>
    private static bool IsImportedSource(Issue issue) =>
        !string.IsNullOrWhiteSpace(issue.SourceBatchId) &&
        (issue.SourceBatchId == "seed" || issue.SourceBatchId.StartsWith("IMP-", StringComparison.Ordinal));

    private static bool SameContent(Issue issue, IssueDraft draft, int year)
    {
        if (!string.Equals(issue.Solution, draft.Solution, StringComparison.Ordinal)) return false;
        if (!string.Equals(issue.CompletionText, draft.CompletionText, StringComparison.Ordinal)) return false;
        foreach (var code in StageCodes.All)
        {
            var existing = issue.Stage(code);
            var incoming = code switch
            {
                StageCodes.Plan => draft.Plan,
                StageCodes.Design => draft.Design,
                StageCodes.Material => draft.Material,
                _ => draft.Assembly
            };
            if (existing == null) return false;
            if (!string.Equals(existing.OwnerText, incoming.OwnerText, StringComparison.Ordinal)) return false;
            if (!SameDue(existing, incoming.DueRaw, year)) return false;
        }
        return true;
    }

    /// <summary>
    /// 计划时间等价判断：原文相同即相同；库内只有语义日期（早期种子/系统录入）时，
    /// 把导入原文解析后与 due_date 比较，避免"同一天、一轮往返就报更新"的假差异。
    /// </summary>
    private static bool SameDue(IssueStage existing, string incomingRaw, int year)
    {
        if (string.Equals(existing.DueRaw, incomingRaw, StringComparison.Ordinal)) return true;
        if (!string.IsNullOrWhiteSpace(existing.DueRaw)) return false;
        if (!existing.DueDate.HasValue) return false;
        var parsed = RawDateParser.Parse(incomingRaw, year);
        return parsed.HasValue && parsed.Value == existing.DueDate.Value;
    }

    private static string Key(string machineCode, string title) =>
        TextUtil.Normalize(machineCode).ToUpperInvariant() + "\u0001" + TextUtil.Normalize(title);

    private static List<int> FindSeqGaps(List<int> values)
    {
        if (values.Count == 0) return new List<int>();
        var max = values.Max();
        var present = values.ToHashSet();
        return Enumerable.Range(1, Math.Min(max, 200)).Where(n => !present.Contains(n)).ToList();
    }

    private static int YearFromName(string fileName)
    {
        var digits = new string(fileName.Where(char.IsDigit).ToArray());
        for (var i = 0; i + 8 <= digits.Length; i++)
        {
            if (int.TryParse(digits.Substring(i, 4), out var year) &&
                year is >= 2000 and <= 2100 &&
                int.TryParse(digits.Substring(i + 4, 2), out var month) && month is >= 1 and <= 12)
                return year;
        }
        for (var i = 0; i + 4 <= digits.Length; i++)
        {
            if (int.TryParse(digits.Substring(i, 4), out var year) && year is >= 2000 and <= 2100)
                return year;
        }
        return AppTime.Today.Year;
    }

    private static ISheet PickSheet(IWorkbook workbook)
    {
        ISheet? fallback = null;
        for (var i = 0; i < workbook.NumberOfSheets; i++)
        {
            var sheet = workbook.GetSheetAt(i);
            fallback ??= sheet;
            if (DetectHeader(sheet) != null) return sheet;
        }
        return fallback ?? throw new InvalidOperationException("工作簿里没有工作表。");
    }

    private static HeaderMap? DetectHeader(ISheet sheet)
    {
        var scanTo = Math.Min(sheet.LastRowNum, HeaderScanRows);
        var maxCol = 0;
        for (var r = 0; r <= scanTo; r++)
        {
            var row = sheet.GetRow(r);
            if (row != null) maxCol = Math.Max(maxCol, row.LastCellNum);
        }

        var titleRow = -1;
        for (var r = 0; r <= scanTo; r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;
            var texts = Enumerable.Range(0, maxCol)
                .Select(c => Norm(CellText(row.GetCell(c))))
                .ToList();
            if (texts.Any(IsMachineHeader) && texts.Any(IsTitleHeader))
            {
                titleRow = r;
                break;
            }
        }
        if (titleRow < 0) return null;

        // 基础列先按标题行定位，用于判断下一行到底是子表头还是数据行。
        var guessMachine = -1;
        var guessTitle = -1;
        for (var col = 0; col < maxCol; col++)
        {
            var head = Norm(GroupText(sheet, titleRow, col));
            if (guessMachine < 0 && IsMachineHeader(head)) guessMachine = col;
            if (guessTitle < 0 && IsTitleHeader(head)) guessTitle = col;
        }

        // 两级表头既可能"阶段组在上、责任人/计划在下"，也可能反过来；
        // 导出的原表版式正是前者（第 1 行阶段组，第 2 行责任人/计划时间）。
        // 旧实现只往标题行上面找列组，导致这类文件的阶段列全部丢失，
        // 往返导入时会把库里的责任人/计划清空——必须把下一行子表头也识别进来。
        var subRow = -1;
        var probeRow = titleRow + 1;
        if (probeRow <= sheet.LastRowNum)
        {
            var probe = sheet.GetRow(probeRow);
            // 数据行的机台号列一定是 P7391xxx 这类真实值，命中即不是子表头。
            var machineText = probe != null && guessMachine >= 0
                ? Norm(CellText(probe.GetCell(guessMachine)))
                : "";
            var looksLikeData = machineText.Length > 0 && TextUtil.LooksLikeMachineCode(machineText);
            if (probe != null && !looksLikeData)
            {
                var hits = 0;
                for (var col = 0; col < maxCol; col++)
                {
                    var text = Norm(CellText(probe.GetCell(col)));
                    if (text.Length == 0) continue;
                    if (IsOwnerHeader(text) || IsDueHeader(text) || DetectStage(text) != null) hits++;
                }
                if (hits >= 2) subRow = probeRow;
            }
        }

        // 兼容旧的"阶段组在标题行上方"布局；同一种文件只会有一种布局。
        var groupRow = -1;
        for (var r = titleRow - 1; r >= 0; r--)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;
            var texts = Enumerable.Range(0, maxCol)
                .Select(c => Norm(CellText(row.GetCell(c))))
                .ToList();
            if (texts.Any(t => DetectStage(t) != null))
            {
                groupRow = r;
                break;
            }
        }
        if (groupRow < 0 && subRow >= 0) groupRow = titleRow;

        var map = new HeaderMap
        {
            TitleRow = titleRow,
            SubRow = subRow,
            GroupRow = groupRow,
            DataStartRow = (subRow >= 0 ? subRow : titleRow) + 1,
            MaxCol = maxCol,
            Seq = -1,
            Machine = -1,
            Title = -1,
            Solution = -1,
            Completion = -1
        };

        for (var col = 0; col < maxCol; col++)
        {
            // 同一列可能同时带"阶段组名"和"责任人/计划时间"，也可能带"方案确定-责任人"
            // 这类扁平表头，因此把标题行、子表头行、上方列组行的文本合并判断。
            var texts = new List<string>();
            void AddText(string? value)
            {
                var text = Norm(value);
                if (text.Length > 0 && !texts.Contains(text)) texts.Add(text);
            }

            AddText(GroupText(sheet, titleRow, col));
            if (subRow >= 0) AddText(CellText(sheet.GetRow(subRow)?.GetCell(col)));
            if (groupRow >= 0 && groupRow != titleRow) AddText(GroupText(sheet, groupRow, col));
            if (texts.Count == 0) continue;

            var stage = texts.Select(DetectStage).FirstOrDefault(s => s.HasValue);
            var isOwner = texts.Any(IsOwnerHeader);
            var isDue = texts.Any(IsDueHeader);
            if (stage.HasValue && (isOwner || isDue))
            {
                if (isOwner) map.Owner[stage.Value] = col;
                else map.Due[stage.Value] = col;
                continue;
            }

            var head = texts[0];
            if (IsSeqHeader(head)) { map.Seq = col; continue; }
            if (IsMachineHeader(head)) { map.Machine = col; continue; }
            if (IsTitleHeader(head)) { map.Title = col; continue; }
            if (IsSolutionHeader(head)) { map.Solution = col; continue; }
            if (IsCompletionHeader(head)) { map.Completion = col; continue; }
        }

        // 说明行（导出模板第 3 行的"××原文"、部分原表的"备注"行）不是数据：
        // 若不跳过，机台号列会读到"格式异常保留原文"并凭空新增一条脏记录。
        for (var probe = 0; probe < 3 && map.DataStartRow <= sheet.LastRowNum; probe++)
        {
            if (!IsNoteRow(sheet, map.DataStartRow, map.Machine, map.Title)) break;
            map.DataStartRow++;
        }

        return map;
    }

    /// <summary>表头之下的说明/备注行：机台号或问题列还是表头文字、或带"原文/保留"字样。</summary>
    private static bool IsNoteRow(ISheet sheet, int rowIndex, int machineCol, int titleCol)
    {
        var row = sheet.GetRow(rowIndex);
        if (row == null) return true;

        var machine = Norm(machineCol >= 0 ? CellText(row.GetCell(machineCol)) : "");
        var title = Norm(titleCol >= 0 ? CellText(row.GetCell(titleCol)) : "");
        if (machine.Length == 0 && title.Length == 0) return true;

        if (machine.Length > 0 &&
            (IsHeaderLike(machine) || machine.Contains("原文") || machine.Contains("保留")))
            return true;

        return title.Length > 0 && (IsHeaderLike(title) || title.Contains("原文"));
    }

    private static bool IsHeaderLike(string text) =>
        IsSeqHeader(text) || IsMachineHeader(text) || IsTitleHeader(text) ||
        IsSolutionHeader(text) || IsCompletionHeader(text) ||
        IsOwnerHeader(text) || IsDueHeader(text);

    private static string GroupText(ISheet sheet, int groupRow, int col)
    {
        for (var i = 0; i < sheet.NumMergedRegions; i++)
        {
            CellRangeAddress region = sheet.GetMergedRegion(i);
            if (region.FirstRow <= groupRow && groupRow <= region.LastRow &&
                region.FirstColumn <= col && col <= region.LastColumn)
                return CellText(sheet.GetRow(region.FirstRow)?.GetCell(region.FirstColumn));
        }
        return CellText(sheet.GetRow(groupRow)?.GetCell(col));
    }

    private static string CellText(ICell? cell)
    {
        if (cell == null) return "";
        try
        {
            if (cell.CellType == CellType.Numeric)
            {
                if (DateUtil.IsCellDateFormatted(cell))
                {
                    var date = cell.DateCellValue;
                    return date.HasValue && date.Value != DateTime.MinValue
                        ? date.Value.ToString("M.d", CultureInfo.InvariantCulture)
                        : "";
                }
                var number = cell.NumericCellValue;
                return number == Math.Floor(number)
                    ? ((long)number).ToString(CultureInfo.InvariantCulture)
                    : number.ToString("0.##", CultureInfo.InvariantCulture);
            }
            if (cell.CellType == CellType.Boolean)
                return cell.BooleanCellValue ? "是" : "否";
            return new DataFormatter().FormatCellValue(cell).Trim();
        }
        catch
        {
            return "";
        }
    }

    private static string Norm(string? text) =>
        TextUtil.Normalize(text).Replace(" ", "").Replace("\t", "").ToLowerInvariant();

    private static bool IsSeqHeader(string text) =>
        text is "序号" or "编号" or "no" or "序" or "行号";

    private static bool IsMachineHeader(string text) =>
        text.Contains("机台", StringComparison.Ordinal) ||
        text.Contains("设备", StringComparison.Ordinal);

    private static bool IsTitleHeader(string text) =>
        text.Contains("问题描述", StringComparison.Ordinal) ||
        text.Contains("存在问题", StringComparison.Ordinal) ||
        text.Contains("问题点", StringComparison.Ordinal) ||
        text.Contains("异常描述", StringComparison.Ordinal) ||
        text == "问题";

    private static bool IsSolutionHeader(string text) =>
        text.Contains("解决措施", StringComparison.Ordinal) ||
        text.Contains("处理措施", StringComparison.Ordinal) ||
        text.Contains("对策", StringComparison.Ordinal) ||
        text.Contains("解决方案", StringComparison.Ordinal);

    private static bool IsCompletionHeader(string text) =>
        text.Contains("完成情况", StringComparison.Ordinal) ||
        text.Contains("关闭说明", StringComparison.Ordinal) ||
        text == "结果";

    private static bool IsOwnerHeader(string text) =>
        text.Contains("责任", StringComparison.Ordinal) ||
        text.Contains("负责", StringComparison.Ordinal) ||
        text.Contains("担当", StringComparison.Ordinal);

    private static bool IsDueHeader(string text) =>
        text.Contains("计划", StringComparison.Ordinal) ||
        text.Contains("纳期", StringComparison.Ordinal) ||
        (text.Contains("日期", StringComparison.Ordinal) && !text.Contains("完成", StringComparison.Ordinal)) ||
        (text.Contains("时间", StringComparison.Ordinal) && !text.Contains("完成", StringComparison.Ordinal));

    /// <summary>方案=0 / 设计=1 / 物料=2 / 装配=3；"调试"按装配处理。</summary>
    private static int? DetectStage(string text)
    {
        if (text.Contains("方案", StringComparison.Ordinal)) return 0;
        if (text.Contains("设计", StringComparison.Ordinal) || text.Contains("下图", StringComparison.Ordinal)) return 1;
        if (text.Contains("物料", StringComparison.Ordinal) || text.Contains("备料", StringComparison.Ordinal)) return 2;
        if (text.Contains("装配", StringComparison.Ordinal) || text.Contains("调试", StringComparison.Ordinal)) return 3;
        return null;
    }

    private static string HashOf(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed class HeaderMap
    {
        public int TitleRow;
        public int SubRow = -1;
        public int GroupRow = -1;
        public int DataStartRow;
        public int MaxCol;
        public int Seq = -1;
        public int Machine = -1;
        public int Title = -1;
        public int Solution = -1;
        public int Completion = -1;
        public int[] Owner = { -1, -1, -1, -1 };
        public int[] Due = { -1, -1, -1, -1 };
    }
}
