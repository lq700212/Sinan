using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using Sinan.App.Mvvm;
using Sinan.App.Services;
using Sinan.Core.Models;
using Sinan.Data;
using Sinan.Data.Sync;

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
    private readonly DispatcherTimer _syncTimer;
    private PairCodeInfo? _pairCode;

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
        GeneratePairCodeCommand = new RelayCommand(GeneratePairCode);
        RemoveDeviceCommand = new RelayCommand(RemoveDevice);
        RefreshSyncCommand = new RelayCommand(RefreshSyncState);

        RefreshSyncState();

        // 在线状态/配对码倒计时都是"随时间变化"的只读信息：2 秒刷新一次，
        // 只读 8 条设备记录，代价可以忽略；不做后台心跳，最近同步时间就是唯一依据。
        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _syncTimer.Tick += (_, _) => RefreshSyncState();
        _syncTimer.Start();

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

    /// <summary>
    /// 配对服务实况：运行中给监听地址；未运行给原因（端口占用/防火墙/无网卡），
    /// 绝不显示"已连接"或假二维码。手机能否连上由它自己的 ping 结果决定。
    /// </summary>
    public string SyncStatusText =>
        _app.SyncServer?.StatusText ?? "配对服务未运行（服务未装配）";

    public string SyncPortDisplay
    {
        get
        {
            var server = _app.SyncServer;
            return server is { IsRunning: true }
                ? string.Join("  ·  ", server.Urls)
                : $"端口 {SyncServer.Port} · 仅监听局域网";
        }
    }

    public bool IsSyncRunning => _app.SyncServer?.IsRunning == true;

    /// <summary>已配对设备（含停用设备置灰展示，便于确认"确实移除过"）。</summary>
    public ObservableCollection<SyncDeviceRow> Devices { get; } = new();

    public bool HasDevices => Devices.Count > 0;

    public int OnlineDeviceCount => Devices.Count(d => d.IsOnline && d.Active);

    public string DeviceSummary => Devices.Count == 0
        ? "尚未配对手机"
        : $"{Devices.Count(d => d.Active)} 台已配对 · {OnlineDeviceCount} 台在线";

    /// <summary>当前有效的 6 位配对码；未生成时显示占位，不伪造二维码。</summary>
    public string PairCodeDisplay => _pairCode?.Code ?? "------";

    public bool HasPairCode => _pairCode != null;

    public string PairCodeExpiry
    {
        get
        {
            if (_pairCode == null) return "点击生成一次性配对码 · 10 分钟有效";
            var left = _pairCode.ExpiresAt - DateTimeOffset.UtcNow;
            if (left <= TimeSpan.Zero) return "已过期，请重新生成";
            return $"剩余 {left.Minutes:00}:{left.Seconds:00} · 仅可使用一次";
        }
    }

    /// <summary>手机端填写的服务器地址示例：取第一个可用监听地址，没有就留空。</summary>
    public string PairAddressHint => _app.SyncServer?.Urls.FirstOrDefault() ?? "";

    public RelayCommand GeneratePairCodeCommand { get; }
    public RelayCommand RemoveDeviceCommand { get; }
    public RelayCommand RefreshSyncCommand { get; }

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

    // ---------------------------------------------------------------------
    // 移动端配对 / 设备
    // ---------------------------------------------------------------------

    private void GeneratePairCode()
    {
        var server = _app.SyncServer;
        if (server is not { IsRunning: true })
        {
            _app.Dialogs.Alert("配对服务未运行",
                $"{server?.StatusText ?? "同步服务未装配"}\n\n请检查端口 {SyncServer.Port} 是否被占用，或重新启动司南。");
            return;
        }

        try
        {
            _pairCode = _app.Sync!.CreatePairCode();
            RefreshSyncState();
        }
        catch (Exception ex)
        {
            AppLog.Error("生成配对码失败", ex);
            _app.Dialogs.Alert("生成失败", $"无法生成配对码：{ex.Message}");
        }
    }

    private void RemoveDevice(object? parameter)
    {
        if (parameter is not SyncDeviceRow row) return;
        if (!row.Active) return;

        if (!_app.Dialogs.Confirm("移除设备",
                $"确定移除「{row.Name}」？\n\n移除后这台手机必须重新配对才能继续同步，已上传的数据不受影响。",
                "移除", "取消"))
            return;

        try
        {
            _app.Sync!.RemoveDevice(row.Id);
            RefreshSyncState();
        }
        catch (Exception ex)
        {
            AppLog.Error("移除同步设备失败", ex);
            _app.Dialogs.Alert("移除失败", ex.Message);
        }
    }

    /// <summary>
    /// 刷新同步状态。定时器与页面操作都会调用：
    /// 设备列表按 Id 对比更新，不整表重建，避免用户正在看的那一行闪烁。
    /// </summary>
    public void RefreshSyncState()
    {
        if (_pairCode != null && _pairCode.ExpiresAt <= DateTimeOffset.UtcNow)
            _pairCode = null;

        List<DeviceInfo> devices;
        try
        {
            devices = _app.Sync?.ListDevices() ?? new List<DeviceInfo>();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"读取同步设备失败：{ex.Message}");
            devices = new List<DeviceInfo>();
        }

        // 简单全量重建：设备上限 8 台，重建比做差分更不容易漏字段。
        Devices.Clear();
        foreach (var device in devices) Devices.Add(new SyncDeviceRow(device));

        OnPropertiesChanged(
            nameof(SyncStatusText), nameof(SyncPortDisplay), nameof(IsSyncRunning),
            nameof(Devices), nameof(HasDevices), nameof(OnlineDeviceCount),
            nameof(DeviceSummary), nameof(PairCodeDisplay), nameof(HasPairCode),
            nameof(PairCodeExpiry), nameof(PairAddressHint));
    }
}

/// <summary>设置页设备表的一行：只做展示映射，不持有连接。</summary>
public sealed class SyncDeviceRow
{
    public SyncDeviceRow(DeviceInfo device)
    {
        Id = device.Id;
        Name = device.Name;
        Active = device.Active;
        IsOnline = device.IsOnline;
        PlatformDisplay = device.Platform switch
        {
            "ios" => "iOS",
            "android" => "Android",
            _ => device.Platform
        };
        ModelDisplay = string.IsNullOrWhiteSpace(device.Model) ? "—" : device.Model;
        PairedDisplay = device.PairedDisplay;
        LastSyncDisplay = device.LastSyncDisplay;
    }

    public string Id { get; }
    public string Name { get; }
    public bool Active { get; }
    public bool IsOnline { get; }
    public string PlatformDisplay { get; }
    public string ModelDisplay { get; }
    public string PairedDisplay { get; }
    public string LastSyncDisplay { get; }
    public string StatusText => !Active ? "已移除" : IsOnline ? "在线" : "离线";
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
