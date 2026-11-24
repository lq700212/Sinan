using System;
using System.Windows;
using System.Windows.Threading;
using Kaleidoscope.UI.Views;
using Sinan.App.Mvvm;
using Sinan.App.Services;
using Sinan.App.ViewModels;

namespace Sinan.App.Views;

/// <summary>
/// 主壳窗口。code-behind 只保留窗口级 glue：
/// 最小化/最大化/关闭、登出换窗、新建问题弹窗、异步命令异常兜底。
/// 导航与页面生命周期全部在 <see cref="ShellViewModel"/>。
/// </summary>
public partial class MainWindow : ChromeBorderlessWindow
{
    private readonly ShellViewModel _shell;
    private bool _switchingWindow;

    public MainWindow()
    {
        InitializeComponent();

        _shell = new ShellViewModel(App.Instance);
        DataContext = _shell;

        _shell.SignOutRequested += OnSignOutRequested;
        _shell.NewIssueRequested += OnNewIssueRequested;
        AsyncRelayCommand.UnhandledError += OnAsyncCommandError;
        Closed += OnClosed;

#if DEBUG
        // 圆角自检（库 Kaleidoscope.UI.Views.KxRadiusAudit，V2.3.8 起）：
        // 自动扫可视树里"CornerRadius 超过短边一半"的 Border——WPF 会把横/纵半径分别钳制，
        // 端头画成椭圆，就是用户说的"圆角太尖"。命中只写日志，不弹窗、不打断流程。
        // 首屏在 ContentRendered 后扫；页面切换等布局完成（ContextIdle）再扫，避免 ActualWidth=0 漏检。
        ContentRendered += (_, _) => AuditRoundedCorners("首屏渲染");
        _shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ShellViewModel.CurrentPage)) return;
            Dispatcher.BeginInvoke(new Action(() => AuditRoundedCorners("页面切换")),
                DispatcherPriority.ContextIdle);
        };
#endif
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnNewIssueRequested(object? sender, EventArgs e)
    {
        var dialog = new NewIssueDialog { Owner = this };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.CreatedIssueId))
        {
            // 新建后壳层计数/台账徽标必须跟着变，否则仍显示旧条数（36 条）。
            _shell.NotifyDataChanged();
            _shell.NavigateToIssue(dialog.CreatedIssueId);
        }
    }

    private void OnSignOutRequested(object? sender, EventArgs e)
    {
        if (!App.Instance.Dialogs.Confirm("退出登录", "确定退出当前账号？\n\n本机数据不会被删除，重新登录即可继续。"))
            return;

        AppLog.Info($"退出登录：{App.Instance.Session.DisplayName}");
        _switchingWindow = true;
        App.Instance.Session.SignOut();

        var login = new LoginWindow();
        Application.Current.MainWindow = login;
        login.Show();
        Close();
    }

    private void OnAsyncCommandError(object? sender, Exception ex)
    {
        // 命令内已记录日志；这里只补一个可读提示，绝不让进程退出。
        Dispatcher.Invoke(() =>
            App.Instance.Dialogs.Alert("操作未完成", ex.Message));
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        AsyncRelayCommand.UnhandledError -= OnAsyncCommandError;

        // 登出时新登录窗已经接管；用户直接关主窗则整个应用退出。
        if (!_switchingWindow) Application.Current.Shutdown();
    }

#if DEBUG
    /// <summary>
    /// 开发期圆角自检：扫描当前可视树（含 ControlTemplate 展开的元素），
    /// 把"半径超过短边一半、会被 WPF 画成椭圆"的 Border 写进日志。
    /// 只在 Debug 构建编译；发布版零开销。判据与修复方式见库内 KxRadiusAudit 类注释。
    /// </summary>
    private void AuditRoundedCorners(string stage)
    {
        int hits = KxRadiusAudit.Scan(this, msg => AppLog.Warn($"[圆角自检·{stage}] {msg}"));
        if (hits > 0)
            AppLog.Warn($"[圆角自检·{stage}] 共 {hits} 处会被画成椭圆，改用 KxCapsuleBorder / CapsuleRadiusConverter。");
        else
            AppLog.Info($"[圆角自检·{stage}] 通过：未发现会被画成椭圆的圆角。");
    }
#endif
}
