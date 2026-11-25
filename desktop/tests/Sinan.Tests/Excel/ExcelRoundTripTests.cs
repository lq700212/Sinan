using System.IO;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using Sinan.Core.Models;
using Sinan.Core.Utils;
using Sinan.Data.Excel;
using Sinan.Tests.Infrastructure;
using Xunit;

namespace Sinan.Tests.Excel;

/// <summary>
/// Excel 通道：三种版式导出、原文无损、文件名净化、撞名不覆盖、扁平表头导入、脏数据保留。
/// 对应 desktop/AGENTS.md 第 6.1 节 8 条无损验收。
/// </summary>
public class ExcelRoundTripTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sinan-xlsx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Export_原表版式_36条()
    {
        using var db = new TempDb();
        var dir = TempDir();
        try
        {
            var result = db.Export.Export(new ExportRequest { Layout = "original" }, db.Engineer);

            Assert.True(File.Exists(result.OutputPath));
            Assert.EndsWith(".xlsx", result.OutputPath);
            Assert.Equal(36, result.IssueCount);
            Assert.True(result.FileSize > 0);
            Assert.StartsWith(db.Db.Paths.ArchiveDir, result.OutputPath);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData("standard")]
    [InlineData("summary")]
    public void Export_标准与汇总版式_可生成(string layout)
    {
        using var db = new TempDb();
        var result = db.Export.Export(new ExportRequest { Layout = layout }, db.Engineer);
        Assert.True(File.Exists(result.OutputPath));
        Assert.Equal(36, result.IssueCount);
    }

    [Fact]
    public void Export_三种版式_文件名与内容不同()
    {
        using var db = new TempDb();
        var a = db.Export.Export(new ExportRequest { Layout = "original" }, db.Engineer);
        var b = db.Export.Export(new ExportRequest { Layout = "standard" }, db.Engineer);
        var c = db.Export.Export(new ExportRequest { Layout = "summary" }, db.Engineer);

        Assert.Equal(3, new[] { a.OutputPath, b.OutputPath, c.OutputPath }.Distinct().Count());
        Assert.Contains("原表版式", a.OutputPath);
        Assert.Contains("标准明细", b.OutputPath);
        Assert.Contains("统计汇总", c.OutputPath);
    }

    [Fact]
    public void Export_同一分钟连续导出_不互相覆盖()
    {
        using var db = new TempDb();
        var request = new ExportRequest { Layout = "standard", PreferredFileName = "回归撞名.xlsx" };

        var first = db.Export.Export(request, db.Engineer);
        var second = db.Export.Export(request, db.Engineer);
        var third = db.Export.Export(request, db.Engineer);

        Assert.NotEqual(first.OutputPath, second.OutputPath);
        Assert.NotEqual(second.OutputPath, third.OutputPath);
        Assert.EndsWith("回归撞名.xlsx", first.OutputPath);
        Assert.EndsWith("回归撞名_2.xlsx", second.OutputPath);
        Assert.EndsWith("回归撞名_3.xlsx", third.OutputPath);
        Assert.True(File.Exists(first.OutputPath));
        Assert.True(File.Exists(second.OutputPath));
        Assert.True(File.Exists(third.OutputPath));
    }

    [Theory]
    [InlineData("..\\..\\evil.xlsx", "evil.xlsx")]
    [InlineData("../../evil.xls", "evil.xlsx")]
    [InlineData("..", "P7391_标准明细")] // 纯点号被挡掉，回落到自动命名
    public void Export_文件名剥离路径_不写出导出目录(string preferred, string expectedEnd)
    {
        using var db = new TempDb();
        var request = new ExportRequest { Layout = "standard", PreferredFileName = preferred };
        var result = db.Export.Export(request, db.Engineer);

        var exportsDir = Path.GetFullPath(Path.Combine(db.Db.Paths.ArchiveDir, "exports"));
        Assert.StartsWith(exportsDir, Path.GetFullPath(result.OutputPath), StringComparison.OrdinalIgnoreCase);
        if (expectedEnd != "P7391_标准明细")
            Assert.EndsWith(expectedEnd, result.OutputPath);
        else
            Assert.Contains(expectedEnd, Path.GetFileName(result.OutputPath));
    }

    [Fact]
    public void Export_原表往返导入_36条全部无变化()
    {
        using var db = new TempDb();
        var exported = db.Export.Export(new ExportRequest { Layout = "original" }, db.Engineer);

        var preview = db.Import.Preview(exported.OutputPath);

        Assert.Equal(36, preview.Total);
        Assert.Equal(0, preview.ErrorCount);
        Assert.Equal(0, preview.ConflictCount);
        Assert.Equal(36, preview.UnchangedCount);
        Assert.Equal(0, preview.InsertCount);
        Assert.Equal(0, preview.UpdateCount);
    }

    [Fact]
    public void Export_日期原文11点5_逐字保留()
    {
        using var db = new TempDb();
        var exported = db.Export.Export(new ExportRequest { Layout = "original" }, db.Engineer);

        var texts = ReadAllStrings(exported.OutputPath);
        Assert.Contains(texts, t => t == "11.5");        // 不能被格式化成 2026-11-05
        Assert.DoesNotContain(texts, t => t == "2026-11-05");
        Assert.Contains(texts, t => t == "已下图");      // 文本计划原样保留
        Assert.Contains(texts, t => t == "/");           // 不适用不能变成空白
    }

    [Fact]
    public void Import_扁平表头_保留脏数据与原文语义()
    {
        using var db = new TempDb();
        var file = BuildFlatXlsx();

        try
        {
            var preview = db.Import.Preview(file);
            Assert.Equal(2, preview.Total);
            Assert.Equal(2, preview.InsertCount);
            Assert.Equal(0, preview.ErrorCount);
            Assert.Equal(1, preview.MachineWarningCount); // P73913118
            Assert.Equal(1, preview.InvertedRowCount);    // 设计 11.25 晚于装配 11.20
            Assert.Equal(1, preview.TextPlanRowCount);    // 已下图

            var result = db.Import.Apply(preview, "merge", db.Engineer);
            Assert.Equal(2, result.Inserted);
            Assert.Equal(0, result.Skipped);
            Assert.StartsWith("IMP-", result.BatchId);

            Assert.Equal(38, db.Issues.All().Count);

            // A：四人/斜杠/已下图/斜杠计划 全部按原表语义落库
            var a = db.Issues.All().Single(i => i.Title == "回归测试：气缸到位信号闪烁");
            Assert.Equal("P7391399", a.MachineCode);
            Assert.Equal("P7391-03", a.SubprojectCode);
            Assert.Equal("11.5", a.PlanStage!.DueRaw);
            Assert.Equal("张工", a.PlanStage.OwnerText);
            Assert.Equal(StageState.InProgress, a.PlanStage.State);
            Assert.Equal("李工/王工", a.DesignStage!.OwnerText); // 多人原文不拆
            Assert.Equal("已下图", a.DesignStage.DueRaw);
            Assert.Null(a.DesignStage.DueDate);
            Assert.Equal(StageState.Done, a.DesignStage.State);
            Assert.Equal("/", a.MaterialStage!.DueRaw);
            Assert.Equal(StageState.NotApplicable, a.MaterialStage.State);
            Assert.Equal("11.20", a.AssemblyStage!.DueRaw);
            Assert.Equal(StageState.InProgress, a.AssemblyStage.State);
            Assert.Equal(IssueStatuses.Planning, a.Status);
            Assert.NotNull(a.SourceBatchId);
            Assert.NotNull(a.SourceRow);

            // B：机台号笔误原样保留、归入待确认，不静默改正
            var b = db.Issues.All().Single(i => i.Title == "回归测试：电柜门锁松动");
            Assert.Equal("P73913118", b.MachineCode);
            Assert.Null(b.SubprojectId);

            // 原文件已归档
            var archived = Directory.GetFiles(Path.Combine(db.Db.Paths.ArchiveDir, "imports"),
                "*" + Path.GetFileName(file));
            Assert.Single(archived);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(file)!, true);
        }
    }

    [Fact]
    public void Import_再次导入同一文件_第二次全部无变化()
    {
        using var db = new TempDb();
        var file = BuildFlatXlsx();
        try
        {
            var first = db.Import.Preview(file);
            db.Import.Apply(first, "merge", db.Engineer);

            var second = db.Import.Preview(file);
            Assert.Equal(2, second.Total);
            Assert.Equal(2, second.UnchangedCount);
            Assert.Equal(0, second.InsertCount);
            Assert.Equal(0, second.UpdateCount);
            Assert.Contains(second.Messages, m => m.Contains("导入过", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(file)!, true);
        }
    }

    [Fact]
    public void Import_只新增策略_已存在记录不覆盖()
    {
        using var db = new TempDb();
        var file = BuildFlatXlsx();
        try
        {
            db.Import.Apply(db.Import.Preview(file), "merge", db.Engineer);
            var issue = db.Issues.All().Single(i => i.Title == "回归测试：气缸到位信号闪烁");
            var originalSolution = issue.Solution;

            // 改原表内容后，用 only_new 再导一次
            // 注意：NPOI 读入后会持有并关闭传入流，必须读入内存后重新用新流写回。
            IWorkbook changed;
            using (var read = File.OpenRead(file))
                changed = WorkbookFactory.Create(read);
            changed.GetSheetAt(0).GetRow(1).GetCell(3).SetCellValue("被 only_new 丢弃的新措施");
            using (var write = new FileStream(file, FileMode.Create, FileAccess.Write))
                changed.Write(write);
            changed.Close();

            var preview = db.Import.Preview(file);
            var updateRow = preview.Rows.Single(r => r.Title == "回归测试：气缸到位信号闪烁");
            Assert.Equal(ImportRowKind.Update, updateRow.Kind);

            var result = db.Import.Apply(preview, "only_new", db.Engineer);
            Assert.Equal(0, result.Updated);
            Assert.Equal(originalSolution,
                db.Issues.Get(issue.Id)!.Solution);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(file)!, true);
        }
    }

    [Fact]
    public void Import_无表头文件_抛异常()
    {
        using var db = new TempDb();
        var dir = TempDir();
        var file = Path.Combine(dir, "随便写的.txt");
        File.WriteAllText(file, "这不是 Excel");
        try
        {
            Assert.Throws<InvalidOperationException>(() => db.Import.Preview(file));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Import_表头缺问题描述列_抛异常()
    {
        using var db = new TempDb();
        var dir = TempDir();
        var file = Path.Combine(dir, "缺列.xlsx");
        var wb = new XSSFWorkbook();
        var sheet = wb.CreateSheet("Sheet1");
        var row = sheet.CreateRow(0);
        row.CreateCell(0).SetCellValue("序号");
        row.CreateCell(1).SetCellValue("机台号");
        using (var fs = new FileStream(file, FileMode.Create, FileAccess.Write))
            wb.Write(fs);
        wb.Close();

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => db.Import.Preview(file));
            Assert.Contains("表头", ex.Message);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Import_范围过滤_倒挂与逾期与StageRules一致()
    {
        using var db = new TempDb();
        var all = db.Issues.All();

        var inverted = db.Export.Export(new ExportRequest { Layout = "standard", Scope = "inverted" }, db.Engineer);
        Assert.Equal(all.Count(i => i.IsInverted), inverted.IssueCount);

        var overdue = db.Export.Export(new ExportRequest { Layout = "standard", Scope = "overdue" }, db.Engineer);
        Assert.Equal(all.Count(i => i.OverdueDays > 0), overdue.IssueCount);

        var selected = db.Export.Export(new ExportRequest
        {
            Layout = "standard",
            SelectAll = false,
            IssueIds = new List<string> { all[0].Id, all[1].Id }
        }, db.Engineer);
        Assert.Equal(2, selected.IssueCount);
    }

    /// <summary>
    /// 构造"扁平表头"原表：一行表头 + 两行数据。
    /// A 行覆盖 11.5 / 已下图 / 斜杠 / 多人责任人 / 倒挂；B 行覆盖已知笔误机台号。
    /// </summary>
    private static string BuildFlatXlsx()
    {
        var dir = TempDir();
        var file = Path.Combine(dir, "20261124_回归测试表.xlsx");

        var wb = new XSSFWorkbook();
        var sheet = wb.CreateSheet("问题清单");
        var headers = new[]
        {
            "序号", "机台号", "问题描述", "解决措施",
            "方案责任人", "方案计划", "设计责任人", "设计计划",
            "物料责任人", "物料计划", "装配责任人", "装配计划", "完成情况"
        };
        var header = sheet.CreateRow(0);
        for (var i = 0; i < headers.Length; i++) header.CreateCell(i).SetCellValue(headers[i]);

        var a = sheet.CreateRow(1);
        var aValues = new[]
        {
            "1", "P7391399", "回归测试：气缸到位信号闪烁", "更换接近开关并调整间隙",
            "张工", "11.5", "李工/王工", "已下图", "赵工", "/", "王工", "11.20", ""
        };
        for (var i = 0; i < aValues.Length; i++) a.CreateCell(i).SetCellValue(aValues[i]);

        var b = sheet.CreateRow(2);
        var bValues = new[]
        {
            "2", "P73913118", "回归测试：电柜门锁松动", "更换门锁",
            "钱工", "11.6", "钱工", "11.25", "", "", "孙工", "11.20", ""
        };
        for (var i = 0; i < bValues.Length; i++) b.CreateCell(i).SetCellValue(bValues[i]);

        using (var fs = new FileStream(file, FileMode.Create, FileAccess.Write))
            wb.Write(fs);
        wb.Close();
        return file;
    }

    private static List<string> ReadAllStrings(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var wb = WorkbookFactory.Create(stream);
        var texts = new List<string>();
        for (var s = 0; s < wb.NumberOfSheets; s++)
        {
            var sheet = wb.GetSheetAt(s);
            for (var r = sheet.FirstRowNum; r <= sheet.LastRowNum; r++)
            {
                var row = sheet.GetRow(r);
                if (row == null) continue;
                for (var c = row.FirstCellNum; c < row.LastCellNum; c++)
                {
                    var cell = row.GetCell(c);
                    if (cell == null) continue;
                    if (cell.CellType == CellType.String && !string.IsNullOrWhiteSpace(cell.StringCellValue))
                        texts.Add(cell.StringCellValue.Trim());
                    else if (cell.CellType == CellType.Numeric)
                        texts.Add(cell.NumericCellValue.ToString("0.###"));
                }
            }
        }
        wb.Close();
        return texts;
    }
}
