using Sinan.App.Mvvm;

namespace Sinan.App.ViewModels;

/// <summary>侧栏导航项。图标用设计稿的 Unicode 符号（⌂ ◈ ▤ ⇥ ▦ ⚙），不引入图标字体依赖。</summary>
public sealed class NavItem : ObservableObject
{
    private bool _isActive;
    private int _badge;

    public NavItem(string key, string icon, string label)
    {
        Key = key;
        Icon = icon;
        Label = label;
    }

    public string Key { get; }
    public string Icon { get; }
    public string Label { get; }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    /// <summary>右侧计数徽标；0 表示不显示。</summary>
    public int Badge
    {
        get => _badge;
        set
        {
            if (!SetProperty(ref _badge, value)) return;
            // 侧栏绑的是 BadgeText（带 99+ 截断），不是 Badge 本身；
            // 少通知这一个，新建/导入后徽标会停在旧数字。
            OnPropertyChanged(nameof(BadgeText));
            OnPropertyChanged(nameof(HasBadge));
        }
    }

    public bool HasBadge => Badge > 0;
    public string BadgeText => Badge > 99 ? "99+" : Badge.ToString();
}
