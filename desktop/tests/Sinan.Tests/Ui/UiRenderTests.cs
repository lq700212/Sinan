using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sinan.App;
using SinanApp = Sinan.App.App;
using Sinan.App.Services;
using Sinan.App.ViewModels;
using Sinan.App.Views;
using Sinan.Tests.Infrastructure;
using Xunit;

namespace Sinan.Tests.Ui;

/// <summary>
/// UI 离屏渲染冒烟：在 STA 线程里构造真实控件树（真实主题字典 + 真实 36 条种子），
/// 用 RenderTargetBitmap 出图，不创建可见窗口、不抢鼠标键盘焦点。
///
/// 为什么全部塞进一个 Fact：WPF 进程内只允许一个 Application 实例，
/// xUnit 的测试并行会让第二个 new App() 直接抛异常；这里一个 STA 线程跑完全部场景。
///
/// 断言分两层：
/// 1) 像素层：采样颜色数，纯白/纯黑说明没渲染出内容；
/// 2) 文案层：遍历可视树文本，必须命中页面独有文案（"数据体检"等），
///    避免"页面没切换但标题栏一样"造成假绿。
/// </summary>
[Collection("WPF-UI")]
public class UiRenderTests
{
    private static readonly string ShotsDir =
        Path.Combine(Path.GetTempPath(), "sinan-ui-render");

    [Fact]
    public void 主壳六页与全部弹窗_明暗双主题_可离屏渲染()
    {
        var errors = new List<string>();
        var thread = new Thread(() => Run(errors));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.True(errors.Count == 0, "UI 渲染断言失败：\n" + string.Join("\n", errors));
    }

    private static void Run(List<string> errors)
    {
        Directory.CreateDirectory(ShotsDir);
        TempDb? db = null;
        var previousDataDir = Environment.GetEnvironmentVariable("SINAN_DATA_DIR");
        try
        {
            // 必须先建临时库、把 SINAN_DATA_DIR 指过去，再 new App：
            // WPF 的 Application 构造函数会把 OnStartup 排进 Dispatcher 队列，
            // 测试里一旦有人泵 Dispatcher（Dispatcher.Invoke 等）就会真的执行组合根，
            // 重建真实 Database 并 Show 登录窗。环境变量兜底保证即使误触发，
            // 建库/日志也落在临时目录，绝不污染 %LOCALAPPDATA%\Sinan。
            db = new TempDb();
            Environment.SetEnvironmentVariable("SINAN_DATA_DIR", db.Paths.Root);

            var app = new SinanApp();
            app.InitializeComponent();

            Inject(app, nameof(SinanApp.Database), db.Db);
            Inject(app, nameof(SinanApp.Users), db.Users);
            Inject(app, nameof(SinanApp.Issues), db.Issues);
            Inject(app, nameof(SinanApp.Projects), db.Projects);
            Inject(app, nameof(SinanApp.Attachments), db.Attachments);
            Inject(app, nameof(SinanApp.Import), db.Import);
            Inject(app, nameof(SinanApp.Export), db.Export);

            app.Session.SignIn(db.Engineer);
            ThemeManager.Initialize(db.Paths.Root);

            var window = new MainWindow();
            var shell = (ShellViewModel)window.DataContext;
            var root = DetachContent(window, 1440, 900);

            // Marker 一律取"该页独有、且不会被外壳标题命中"的文案：
            // 页面按设计稿重构后旧标题（如"优先处理清单""四阶段完成率"）已不存在，
            // 这里跟着界面真源改，避免测试自己变成假红/假绿。
            var pages = new (string Key, string Marker, string File)[]
            {
                (ShellViewModel.PageOverview, "问题分布", "page-overview"),
                (ShellViewModel.PageDashboard, "数据体检", "page-dashboard"),
                (ShellViewModel.PageIssues, "问题清单", "page-issues"),
                (ShellViewModel.PageExchange, "列映射", "page-exchange"),
                (ShellViewModel.PageReport, "计划时间分布", "page-report"),
                (ShellViewModel.PageSettings, "数据源", "page-settings")
            };

            foreach (var (key, marker, file) in pages)
            {
                shell.Navigate(key);
                Render(root, file + "-light.png", marker, errors);
            }

            // 暗色主题：同一批页面重渲染，重点检查表格/滚动条是否露白。
            //
            // 注意：必须重建窗口与内容树。被 DetachContent 摘出来的 Border 已经不在
            // Window 树里，Application.Resources 的字典替换（ThemeManager.Apply）不会
            // 通知游离元素，实测 root.Background 仍是浅色 #FBF6EE（资源字典本身已是
            // 深色 #1C140D），暗色截图会得到"深底 + 浅卡片"的假象。新建的树会按当前
            // 字典解析 DynamicResource，才是真实窗口切换主题后的样子。
            ThemeManager.Apply(ThemeKind.Dark);
            var darkWindow = new MainWindow();
            var darkShell = (ShellViewModel)darkWindow.DataContext;
            var darkRoot = DetachContent(darkWindow, 1440, 900);
            foreach (var (key, marker, file) in pages)
            {
                darkShell.Navigate(key);
                Render(darkRoot, file + "-dark.png", marker, errors);
            }
            ThemeManager.Apply(ThemeKind.Light);

            // 登录窗：品牌位 56×56 与表单同屏。
            var login = new LoginWindow();
            var loginRoot = DetachContent(login, 1440, 900);
            Render(loginRoot, "window-login.png", "登录工作台", errors);

            // 五个业务弹窗 + 新建问题弹窗。
            var stage = new StageDialog();
            stage.Load(new StageDialogRequest
            {
                IssueCode = "P7391-009",
                IssueTitle = "气缸到位信号偶发丢失",
                SelectedCode = "design",
                Stages = new List<StageDialogChoice>
                {
                    new() { Code = "plan", Label = "方案确定", OwnerText = "张工", DueRaw = "11.5", DueDateIso = "2026-11-05" },
                    new() { Code = "design", Label = "设计下图", OwnerText = "李工/王工", DueRaw = "11.12" },
                    new() { Code = "material", Label = "物料准备", DueRaw = "/" },
                    new() { Code = "assembly", Label = "装配调试", OwnerText = "赵工", DueRaw = "11.20" }
                }
            });
            RenderDialog(stage, "dialog-stage.png", "更新阶段", errors);

            var delete = new DeleteIssueDialog();
            delete.Load(new DeleteIssueRequest
            {
                IssueCode = "P7391-009",
                IssueTitle = "气缸到位信号偶发丢失",
                ActivityCount = 6,
                PhotoCount = 2
            });
            RenderDialog(delete, "dialog-delete.png", "删除", errors);

            var photo = new PhotoViewerDialog();
            photo.Load(new PhotoViewerRequest
            {
                IssueCode = "P7391-009",
                IssueTitle = "气缸到位信号偶发丢失",
                MachineCode = "P7391103",
                SubprojectCode = "P7391-01",
                StartIndex = 0,
                Photos = new List<PhotoViewerItem>
                {
                    new()
                    {
                        FileName = "demo-01.jpg", Caption = "气缸原位感应器",
                        TakenAtDisplay = "11-20 14:30", UploadedBy = "张工",
                        SizeDisplay = "1.2 MB", DimensionText = "1600×1200"
                    }
                }
            });
            RenderDialog(photo, "dialog-photo.png", "现场照片", errors);

            var import = new ImportConfirmDialog();
            import.Load(new ImportConfirmRequest
            {
                FileName = "20261124_问题清单.xlsx",
                Total = 36, InsertCount = 2, UpdateCount = 1, UnchangedCount = 32,
                ConflictCount = 1, ErrorCount = 0, InvertedCount = 5,
                TextPlanCount = 1, MachineWarningCount = 1, Strategy = "merge",
                Conflicts = new List<ImportConflictRow>
                {
                    new()
                    {
                        SourceRow = 12, IssueCode = "P7391-012",
                        Title = "装配节拍偏慢",
                        SystemText = "装配计划 11-15",
                        ExcelText = "装配计划 11-01"
                    }
                }
            });
            RenderDialog(import, "dialog-import.png", "确认导入", errors);

            var export = new ExportConfirmDialog();
            export.Load(new ExportConfirmRequest
            {
                FileName = "P7391_原表版式.xlsx",
                Directory = db.Paths.ArchiveDir,
                LayoutLabel = "客户原表版式",
                ScopeLabel = "全部问题",
                IssueCount = 36, InvertedCount = 5, TextPlanCount = 1, PhotoCount = 12,
                IncludeAttachments = true, IncludeChangeLog = true,
                Sheets = new List<string> { "问题台账", "附件清单", "变更记录", "导出说明" }
            });
            RenderDialog(export, "dialog-export.png", "开始导出", errors);

            var newIssue = new NewIssueDialog();
            RenderDialog(newIssue, "dialog-new-issue.png", "新建问题", errors);

            // 圆角自检：对主壳与弹窗跑库内扫描器，命中即记入失败。
            var audit = Kaleidoscope.UI.Views.KxRadiusAudit.Scan(window);
            if (audit > 0) errors.Add($"[圆角自检] 主壳命中 {audit} 处椭圆化圆角");
            var dialogAudit = Kaleidoscope.UI.Views.KxRadiusAudit.Scan(stage);
            if (dialogAudit > 0) errors.Add($"[圆角自检] 阶段弹窗命中 {dialogAudit} 处椭圆化圆角");
        }
        catch (Exception ex)
        {
            errors.Add("渲染过程异常：" + ex);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SINAN_DATA_DIR", previousDataDir);
            db?.Dispose();
        }
    }

    /// <summary>
    /// 把 Window.Content 摘下来单独渲染。
    /// 为什么不直接渲染 Window：未 Show 的 Window 没有 HwndSource，RenderTargetBitmap 只能得到空白；
    /// 而 Show() 会创建可见窗口（可能闪一下/抢焦点），违反后台静默要求。
    /// 摘下来后必须手动补两件事：DataContext（原本靠窗口继承）与 Window.Resources
    /// （MainWindow 的页面 DataTemplate 全在窗口资源里，脱父后查不到）。
    /// </summary>
    private static FrameworkElement DetachContent(Window window, double width, double height)
    {
        var root = window.Content as FrameworkElement
                   ?? throw new InvalidOperationException($"{window.GetType().Name} 没有可渲染的内容根。");
        window.Content = null;
        root.DataContext = window.DataContext;
        root.Resources.MergedDictionaries.Add(window.Resources);
        root.Width = width;
        root.Height = height;
        return root;
    }

    private static void Render(FrameworkElement root, string file, string marker,
        List<string> errors)
    {
        Layout(root);
        var path = Path.Combine(ShotsDir, file);
        var colors = Snapshot(root, path);

        if (colors < 12)
            errors.Add($"{file}: 颜色数仅 {colors}，疑似空白渲染");
        var texts = CollectText(root).ToList();
        if (!texts.Any(t => t.Contains(marker, StringComparison.Ordinal)))
            errors.Add($"{file}: 未找到页面独有文案「{marker}」，当前文本 {texts.Count} 条");
    }

    private static void RenderDialog(Window dialog, string file, string marker,
        List<string> errors)
    {
        // ChromeDialogWindow 会把子类 XAML 的内容转移到骨架 ContentPresenter，
        // 并把 Window.Content 换成骨架根（标题条 + 内容槽）。这里**不能**摘 Content：
        // OnContentChanged 会把内容槽清空（实测只剩标题条、主体全白）。
        // 未显示的窗口允许手动 Measure/Arrange 骨架，标题条与内容槽一起出图。
        var root = dialog.Content as FrameworkElement
                   ?? throw new InvalidOperationException($"{dialog.GetType().Name} 没有内容根。");
        var width = double.IsNaN(dialog.Width) ? 760 : dialog.Width;
        var height = double.IsNaN(dialog.Height) ? 640 : dialog.Height;
        root.Width = width;
        root.Height = height;
        Render(root, file, marker, errors);
    }

    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(root.Width, root.Height));
        root.Arrange(new Rect(0, 0, root.Width, root.Height));
        root.UpdateLayout();

        // 这里刻意不泵 Dispatcher：Application 构造函数把 OnStartup 排在 Dispatcher 队列里，
        // 一泵就会执行组合根（重建真实库 + Show 登录窗），既污染用户数据也会弹窗抢焦点。
        // 主题切换的刷新问题已由"切暗色后重建窗口树"解决，不依赖消息泵。
    }

    private static int Snapshot(FrameworkElement root, string path)
    {
        var width = (int)Math.Ceiling(root.Width);
        var height = (int)Math.Ceiling(root.Height);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) encoder.Save(stream);

        // 20×20 网格采样统计不同颜色；纯色说明布局没跑起来或控件全透明。
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        var colors = new HashSet<int>();
        for (var y = 0; y < height; y += Math.Max(1, height / 20))
        for (var x = 0; x < width; x += Math.Max(1, width / 20))
        {
            var offset = (y * width + x) * 4;
            var argb = (pixels[offset + 3] << 24) | (pixels[offset + 2] << 16) |
                       (pixels[offset + 1] << 8) | pixels[offset];
            colors.Add(argb);
        }
        return colors.Count;
    }

    private static IEnumerable<string> CollectText(DependencyObject node)
    {
        if (node is TextBlock block)
        {
            var text = new TextRange(block.ContentStart, block.ContentEnd).Text.Trim();
            if (text.Length > 0) yield return text;
        }
        else if (node is ContentControl { Content: string content } && content.Length > 0)
        {
            yield return content;
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            foreach (var text in CollectText(VisualTreeHelper.GetChild(node, i)))
                yield return text;
        }
    }

    /// <summary>App 的仓储属性是 private set；测试用反射注入临时库，不改产品代码。</summary>
    private static void Inject(SinanApp app, string property, object value)
    {
        var info = typeof(SinanApp).GetProperty(property, BindingFlags.Public | BindingFlags.Instance)
                   ?? throw new InvalidOperationException($"App.{property} 不存在。");
        var setter = info.GetSetMethod(nonPublic: true)
                     ?? throw new InvalidOperationException($"App.{property} 没有 setter。");
        setter.Invoke(app, new[] { value });
    }
}
