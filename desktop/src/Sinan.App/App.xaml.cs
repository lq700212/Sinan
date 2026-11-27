using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Sinan.App.Services;
using Sinan.App.Views;
using Sinan.Data;
using Sinan.Data.Excel;
using Sinan.Data.Repositories;
using Sinan.Data.Sync;

namespace Sinan.App;

/// <summary>
/// 组合根：数据库 → 仓储 → Excel 通道 → 主题 → 登录窗。
///
/// 为什么不用 DI 容器：整个应用只有十来个单例，构造函数手写装配更直观，
/// 现场排障时"谁依赖谁"一眼可见；引入容器会让启动顺序问题变成黑盒。
/// </summary>
public partial class App : Application
{
    public Database Database { get; private set; } = null!;
    public UserRepository Users { get; private set; } = null!;
    public IssueRepository Issues { get; private set; } = null!;
    public ProjectRepository Projects { get; private set; } = null!;
    /// <summary>当前总项目（多项目切换的唯一真源）；页面与仓储一律从这里取 ProjectId。</summary>
    public ProjectContext ProjectsContext { get; private set; } = null!;
    public AttachmentRepository Attachments { get; private set; } = null!;
    public ExcelImportService Import { get; private set; } = null!;
    public ExcelExportService Export { get; private set; } = null!;
    public SyncRepository? Sync { get; private set; }
    public SyncServer? SyncServer { get; private set; }

    /// <summary>配对二维码会话：设置页与顶栏弹窗共用同一个未使用配对码。</summary>
    public PairingSession Pairing { get; private set; } = null!;

    public Session Session { get; } = new();
    public IDialogService Dialogs { get; private set; } = new DialogService();

    public static App Instance => (App)Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 全局异常兜底：WPF 里漏网的 async void / 事件处理器异常会直接杀掉进程，
        // 现场表现是"点了按钮就退出"。这里记录并提示，避免用户丢数据。
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var paths = DataPaths.CreateDefault();
        AppLog.Initialize(paths.LogsDir);
        AppLog.Info($"司南启动 · 数据根目录 {paths.Root}");

        try
        {
            Database = new Database(paths);
            Database.Initialize();

            Users = new UserRepository(Database);
            Issues = new IssueRepository(Database);
            Projects = new ProjectRepository(Database, Issues);
            ProjectsContext = new ProjectContext(Database, Projects);
            ProjectsContext.Load();
            Attachments = new AttachmentRepository(Database);
            Import = new ExcelImportService(Database, Issues, Projects);
            Export = new ExcelExportService(Database, Issues, Projects, Attachments);
            Sync = new SyncRepository(Database, Issues, Projects, Users);

            // 局域网同步服务：端口固定 5180，仅监听本机局域网 IPv4。
            // 启动失败不阻断主程序，设置页如实显示"未运行 + 原因"。
            SyncServer = new SyncServer(Sync, Attachments);
            SyncServer.Start();
            Pairing = new PairingSession(this);

            // 演示附件（12 张 / 6 条）只登记元数据并标记 is_demo=1；
            // 界面显示"演示"角标，导出 ZIP 只写说明不塞假图。
            var demoCount = Attachments.EnsureDemoAttachments();
            if (demoCount > 0) AppLog.Info($"已补演示附件 {demoCount} 个");

            ThemeManager.Initialize(paths.Root);

            var login = new LoginWindow();
            MainWindow = login;
            login.Show();
        }
        catch (Exception ex)
        {
            AppLog.Error("启动失败", ex);
            MessageBox.Show(
                $"司南启动失败：{ex.Message}\n\n数据目录：{paths.Root}\n日志目录：{paths.LogsDir}",
                "司南", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SyncServer?.Dispose();
        AppLog.Info($"司南退出，代码 {e.ApplicationExitCode}");
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("界面线程未处理异常", e.Exception);
        var logs = Database?.Paths.LogsDir ?? "";
        MessageBox.Show(
            $"操作未完成：{e.Exception.Message}\n\n详细信息已写入日志：\n{logs}",
            "司南", MessageBoxButton.OK, MessageBoxImage.Error);
        // 标记已处理：已经给了用户可读提示，不让进程直接消失。
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) AppLog.Error("后台线程未处理异常", ex);
        else AppLog.Error($"后台线程未处理异常：{e.ExceptionObject}");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Error("未观察的任务异常", e.Exception);
        e.SetObserved();
    }
}
