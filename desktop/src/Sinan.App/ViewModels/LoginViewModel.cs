using System;
using System.Linq;
using System.Threading.Tasks;
using Sinan.App.Mvvm;
using Sinan.App.Services;
using Sinan.Core.Models;
using Sinan.Data;

namespace Sinan.App.ViewModels;

/// <summary>
/// 登录页：工号 + 密码 → 本机 SQLite 验证 → 写入 Session。
///
/// 界面约束（设计稿 p1）：
/// - 失败原因就地展开，不弹二级窗；
/// - 左下角常驻真实数据库路径，便于现场排障（不抄设计稿的 D:\Sinan）；
/// - 左侧四个事实数字全部实时查库，不写死。
/// </summary>
public sealed class LoginViewModel : ObservableObject
{
    private readonly App _app;
    private string _employeeNo;
    private string _password = "";
    private bool _rememberEmployeeNo;
    private string _errorMessage = "";
    private bool _isBusy;

    public LoginViewModel(App app)
    {
        _app = app;
        _employeeNo = app.Session.User?.EmployeeNo ?? "";

        var prefs = UiPrefsStore.Current;
        _rememberEmployeeNo = prefs.RememberEmployeeNo;
        if (_rememberEmployeeNo && !string.IsNullOrWhiteSpace(prefs.LastEmployeeNo))
            _employeeNo = prefs.LastEmployeeNo;

        DataPathDisplay = app.Database.Paths.DbPath;
        DataRootDisplay = app.Database.Paths.Root;
        VersionDisplay = $"{AppInfo.VersionLabel} · SQLite · schema {Database.SchemaVersion}";

        var issues = app.Issues.All();
        IssueCount = issues.Count;
        MachineCount = issues.Select(i => i.MachineId).Distinct().Count();
        SubprojectCount = issues.Where(i => i.SubprojectId != null)
            .Select(i => i.SubprojectId).Distinct().Count();
        StageNodeCount = 4;

        LoginCommand = new RelayCommand(() => _ = SignInAsync(), CanSignIn);
        ShowPairingCommand = new RelayCommand(ShowPairing);
        OpenDataFolderCommand = new RelayCommand(OpenDataFolder);
    }

    public event EventHandler<User>? LoginSucceeded;

    public string EmployeeNo
    {
        get => _employeeNo;
        set
        {
            if (!SetProperty(ref _employeeNo, value)) return;
            ErrorMessage = "";
            LoginCommand.RaiseCanExecuteChanged();
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            _password = value;
            ErrorMessage = "";
        }
    }

    public bool RememberEmployeeNo
    {
        get => _rememberEmployeeNo;
        set => SetProperty(ref _rememberEmployeeNo, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (!SetProperty(ref _errorMessage, value)) return;
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            LoginCommand.RaiseCanExecuteChanged();
        }
    }

    public string DataPathDisplay { get; }
    public string DataRootDisplay { get; }
    public string VersionDisplay { get; }
    public int IssueCount { get; }
    public int MachineCount { get; }
    public int SubprojectCount { get; }
    public int StageNodeCount { get; }

    public string HintText => IsBusy ? "正在验证…" : "使用调试组工号登录；数据保存在本机，断网可用";

    public RelayCommand LoginCommand { get; }
    public RelayCommand ShowPairingCommand { get; }
    public RelayCommand OpenDataFolderCommand { get; }

    /// <summary>PasswordBox 不支持绑定，由 code-behind 在 PasswordChanged 时回传（View 只留 glue）。</summary>
    public void SetPassword(string password)
    {
        Password = password;
        LoginCommand.RaiseCanExecuteChanged();
    }

    private bool CanSignIn() =>
        !IsBusy && !string.IsNullOrWhiteSpace(EmployeeNo) && Password.Length > 0;

    /// <summary>
    /// 手机配对入口。当前 MVP 单机批尚未启用内嵌同步服务，
    /// 这里如实告知，不展示不存在的配对码（根 AGENTS.md 第 7 节第 9 条：不伪造）。
    /// </summary>
    private void ShowPairing()
    {
        _app.Dialogs.Alert("手机端配对",
            "当前版本是离线单机版：桌面端配对服务尚未启用，不会生成配对码。\n\n" +
            "规划中的方式：电脑与手机在同一局域网，桌面端生成 6 位配对码，" +
            "手机端（Flutter）凭码建立信任，之后可离线记录、联网增量上传。\n\n" +
            "本机数据不会上传到任何服务器。");
    }

    private void OpenDataFolder()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _app.Database.Paths.Root,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn($"打开数据目录失败：{ex.Message}");
            _app.Dialogs.Alert("打开失败", $"无法打开数据目录：\n{_app.Database.Paths.Root}");
        }
    }

    private async Task SignInAsync()
    {
        if (!CanSignIn()) return;

        IsBusy = true;
        ErrorMessage = "";
        try
        {
            // 密码校验是 PBKDF2（迭代哈希），放后台线程，避免登录瞬间掉帧。
            var employeeNo = EmployeeNo.Trim();
            var password = Password;
            var user = await Task.Run(() => _app.Users.Authenticate(employeeNo, password));

            if (user == null)
            {
                ErrorMessage = "工号或密码不正确。请确认大小写，或联系管理员重置。";
                Password = "";
                OnPropertyChanged(nameof(HintText));
                return;
            }

            var prefs = UiPrefsStore.Current;
            prefs.RememberEmployeeNo = RememberEmployeeNo;
            prefs.LastEmployeeNo = RememberEmployeeNo ? employeeNo : "";
            UiPrefsStore.Save();

            _app.Session.SignIn(user);
            AppLog.Info($"登录成功：{user.EmployeeNo}（{user.Name} / {user.RoleDisplay}）");
            LoginSucceeded?.Invoke(this, user);
        }
        catch (Exception ex)
        {
            AppLog.Error("登录失败", ex);
            ErrorMessage = $"登录失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HintText));
        }
    }
}
