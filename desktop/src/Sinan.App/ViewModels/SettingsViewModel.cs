using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Sinan.App.Mvvm;
using Sinan.App.Services;
using Sinan.Core.Models;
using Sinan.Data;

namespace Sinan.App.ViewModels;

/// <summary>
/// 系统设置（设计稿 p4 settings）：本地数据、账号、主题、关于。
///
/// 所有路径取运行时实际值（%LOCALAPPDATA%\Sinan 或 SINAN_DATA_DIR 覆盖），
/// 绝不抄设计稿里的示意路径，现场排障时才能照着找到文件。
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly App _app;

    public SettingsViewModel(App app, INavigationService nav)
    {
        _app = app;

        var paths = app.Database.Paths;
        DataRoot = paths.Root;
        DbPath = paths.DbPath;
        AttachmentsDir = paths.AttachmentsDir;
        LogsDir = paths.LogsDir;
        ArchiveDir = paths.ArchiveDir;
        SchemaVersion = $"schema {Database.SchemaVersion} · 程序 {AppInfo.VersionLabel}";

        var issues = app.Issues.All();
        IssueCount = issues.Count;
        MachineCount = issues.Select(i => i.MachineId).Distinct().Count();
        OverdueCount = issues.Count(i => i.OverdueDays > 0);
        InvertedCount = issues.Count(i => i.IsInverted);
        Users = app.Users.ListActive();
        UserCount = Users.Count;

        OpenFolderCommand = new RelayCommand(OpenFolder);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        OpenLogFileCommand = new RelayCommand(OpenLogFolder);

        // 设计稿左侧固定 6 项，右侧是同一页里纵向排列的卡片（点击目录滚动定位），
        // 不做"点一项只显示一卡"的切换，否则页面会空掉大半屏。
        Sections = new[]
        {
            new SettingNavItem("data", "数据与备份"),
            new SettingNavItem("import", "导入规则"),
            new SettingNavItem("alert", "提醒与逾期"),
            new SettingNavItem("people", "人员与角色"),
            new SettingNavItem("mobile", "移动端配对"),
            new SettingNavItem("about", "关于与许可"),
        };
        _sectionItem = Sections[0];
    }

    /// <summary>
    /// 左侧设置目录（设计稿 settings 页）。
    /// 用 ListBox 而不是自绘 RadioButton：选中语义由 SelectionItemPattern 提供，
    /// 读屏与自动化都能定位；样式仍由页面 ItemContainerStyle 控制。
    /// </summary>
    public IReadOnlyList<SettingNavItem> Sections { get; }

    private SettingNavItem? _sectionItem;

    /// <summary>当前选中的目录项；变化后由 SettingsPage.xaml.cs 把对应卡片滚到视口顶部。</summary>
    public SettingNavItem? SelectedSectionItem
    {
        get => _sectionItem;
        set
        {
            if (!SetProperty(ref _sectionItem, value) || value == null) return;
            OnPropertiesChanged(nameof(SelectedSection));
        }
    }

    public string SelectedSection => SelectedSectionItem?.Key ?? "data";

    /// <summary>
    /// 主题变化时由壳层统一回调（ShellViewModel 持有唯一的 ThemeManager.Changed 订阅）。
    /// 本页 VM 会被壳层缓存/重建，自己订阅静态事件会累积泄漏，所以不在这里订阅。
    /// </summary>
    public void RefreshTheme() =>
        OnPropertiesChanged(nameof(ThemeLabel), nameof(ThemeActionLabel));

    public string DataRoot { get; }
    public string DbPath { get; }
    public string AttachmentsDir { get; }
    public string LogsDir { get; }
    public string ArchiveDir { get; }
    public string SchemaVersion { get; }
    public int IssueCount { get; }
    public int MachineCount { get; }
    public int UserCount { get; }
    public int OverdueCount { get; }
    public int InvertedCount { get; }
    public List<User> Users { get; }

    /// <summary>配对服务实况：MVP 单机版没有启动监听，界面必须如实说"未启用"，不放假二维码。</summary>
    public string SyncStatusText => "配对服务尚未启用（当前为离线单机版）";
    public string SyncPortDisplay => "规划端口 5180 · 仅监听局域网";

    public string CurrentUserName => _app.Session.User?.Name ?? "—";
    public string CurrentUserRole => _app.Session.User?.RoleDisplay ?? "—";
    public string CurrentUserEmployeeNo => _app.Session.User?.EmployeeNo ?? "—";

    public string ThemeLabel => ThemeManager.Current == ThemeKind.Dark ? "深色" : "浅色";
    public string ThemeActionLabel => ThemeManager.Current == ThemeKind.Dark ? "切换到浅色" : "切换到深色";

    public string DataSizeText
    {
        get
        {
            try
            {
                var info = new FileInfo(DbPath);
                if (!info.Exists) return "数据库文件尚未生成";
                var mb = info.Length / 1024d / 1024d;
                return $"{mb:0.00} MB · 修改于 {info.LastWriteTime:yyyy-MM-dd HH:mm}";
            }
            catch
            {
                return "无法读取数据库文件大小";
            }
        }
    }

    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }
    public RelayCommand OpenLogFileCommand { get; }

    private void ToggleTheme()
    {
        // 只负责切换；文案刷新交给 ThemeManager.Changed 统一处理，
        // 避免"本页点一次刷新两次、顶栏点一次不刷新"的口径分裂。
        ThemeManager.Toggle();
    }

    private void OpenFolder(object? parameter)
    {
        var path = parameter as string;
        if (string.IsNullOrWhiteSpace(path)) path = DataRoot;

        try
        {
            // 传入的可能是文件（数据库/日志文件），此时打开其所在目录，
            // 绝不能把文件名当目录 CreateDirectory（现场会平白多出一个 sinon.db 文件夹）。
            if (File.Exists(path))
                path = Path.GetDirectoryName(path) ?? DataRoot;

            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn($"打开目录失败：{ex.Message}");
            _app.Dialogs.Alert("打开失败", $"无法打开目录：\n{path}");
        }
    }

    private void OpenLogFolder() => OpenFolder(LogsDir);
}

/// <summary>设置目录的一项：Key 用于定位右侧卡片，Label 是界面文案。</summary>
public sealed class SettingNavItem
{
    public SettingNavItem(string key, string label)
    {
        Key = key;
        Label = label;
    }

    public string Key { get; }
    public string Label { get; }
}
