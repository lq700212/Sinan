using System;
using Sinan.Core.Models;

namespace Sinan.App.Services;

/// <summary>
/// 当前登录会话。整个应用只存在一个实例，由组合根创建、登录页写入、主壳读取。
///
/// 为什么不放静态全局：会话要能被单元测试替换，且退出登录时需要明确清空；
/// 静态字段容易在多次登录之间残留旧身份（尤其"记住工号"场景）。
/// </summary>
public sealed class Session
{
    public User? User { get; private set; }
    public DateTime LoginAt { get; private set; }

    public bool IsAuthenticated => User != null;
    public bool IsAdmin => User?.IsAdmin == true;
    public bool CanEdit => User?.CanEdit == true;
    public string DisplayName => User?.Name ?? "";

    public event EventHandler? Changed;

    public void SignIn(User user)
    {
        User = user ?? throw new ArgumentNullException(nameof(user));
        LoginAt = DateTime.Now;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SignOut()
    {
        User = null;
        LoginAt = default;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
