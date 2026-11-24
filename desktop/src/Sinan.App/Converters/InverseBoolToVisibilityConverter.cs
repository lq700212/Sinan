using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Sinan.App.Converters;

/// <summary>
/// bool → Visibility 的反向版本：true 折叠、false 展开。
///
/// 为什么不用两个转换器串联：WPF 不支持 Binding 结果再进第二个转换器；
/// 而"有数据就隐藏空状态"是详情页最常见的组合，单独实现一个可复用件更清楚。
/// 非 bool 值一律按 false 处理（显示内容），避免绑定异常时把正文藏掉。
/// </summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility visibility && visibility != Visibility.Visible;
}
