namespace Sinan.App.Services;

/// <summary>
/// 程序版本号的唯一读取口。
///
/// 【为什么要有它】登录页、主壳状态条、系统设置关于页三处都要显示 "v0.0.1"，
/// 早期版本各自写死字符串，升 <c>Directory.Build.props</c> 的 &lt;Version&gt; 后界面仍显示旧号，
/// 交付时对不上安装包版本。这里统一从程序集元数据读，版本只改一处。
/// </summary>
public static class AppInfo
{
    /// <summary>形如 "0.0.1"（取AssemblyVersion 前三段；InformationalVersion 可能带 +commit 后缀）。</summary>
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>界面统一显示的版本标签，形如 "v0.0.1"。</summary>
    public static string VersionLabel => $"v{Version}";
}
