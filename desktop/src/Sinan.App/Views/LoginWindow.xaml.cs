using System.Windows;
using System.Windows.Controls;
using Sinan.App.ViewModels;
using Sinan.Core.Models;

namespace Sinan.App.Views;

/// <summary>
/// 登录窗。code-behind 只做三件 glue：
/// ① PasswordBox → ViewModel（WPF 安全设计不支持绑定密码）；
/// ② 登录成功后开主窗并关闭自己；
/// ③ 关闭按钮。
/// 所有业务判断（校验、记住工号、失败文案）都在 <see cref="LoginViewModel"/>。
/// </summary>
public partial class LoginWindow : Kaleidoscope.UI.Views.ChromeBorderlessWindow
{
    private readonly LoginViewModel _viewModel;

    public LoginWindow()
    {
        InitializeComponent();
        _viewModel = new LoginViewModel(App.Instance);
        DataContext = _viewModel;
        _viewModel.LoginSucceeded += OnLoginSucceeded;
        Loaded += (_, _) => EmployeeNoBox.Focus();
        Closed += OnClosed;
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box) _viewModel.SetPassword(box.Password);
    }

    private void OnLoginSucceeded(object? sender, User user)
    {
        AppLog.Info($"打开主窗口：{user.Name}");
        var main = new MainWindow();
        Application.Current.MainWindow = main;
        main.Show();
        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnClosed(object? sender, System.EventArgs e)
    {
        // 用户直接关窗（未登录）→ 整个应用退出；登录成功时主窗已经接管。
        if (!App.Instance.Session.IsAuthenticated)
        {
            Application.Current.Shutdown();
        }
    }
}
