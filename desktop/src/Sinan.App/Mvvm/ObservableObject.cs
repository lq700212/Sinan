using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Sinan.App.Mvvm;

/// <summary>
/// 最小 MVVM 基类：只做属性变更通知。
///
/// 为什么不自建重型框架：司南是单窗口 WPF 应用，需要的只有 INotifyPropertyChanged
/// 与命令两件；引入 CommunityToolkit/PRISM 会让现场排障多一层黑盒，反而增加维护成本。
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>赋值并通知；值相等时返回 false，避免无意义重绘（列表滚动时会大量触发）。</summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>批量通知：一次改动影响多个只读派生属性时用，减少 UI 逐条刷新。</summary>
    protected void OnPropertiesChanged(params string[] names)
    {
        foreach (var name in names) OnPropertyChanged(name);
    }
}
