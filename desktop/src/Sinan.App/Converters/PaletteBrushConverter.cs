using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Sinan.App.Converters;

/// <summary>
/// 主题键 → 画刷：给 XAML 里按数据项切换颜色的小色块用（如总览问题分布图例）。
///
/// 为什么不用 DynamicResource：颜色键来自数据（Brush.Primary / Brush.Primary2 …），
/// StaticResource/DynamicResource 都不能从绑定值解析资源键。转换器在加载时解析一次；
/// 主题切换后页面重建时会重新解析（总览/报表属于统计页，切主题后本来就会重建或刷新）。
/// 找不到键时退主色，绝不抛异常导致整页黑块。
/// </summary>
public sealed class PaletteBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string? key = value as string;
        if (!string.IsNullOrWhiteSpace(key))
        {
            if (Application.Current?.TryFindResource(key) is Brush brush) return brush;
        }
        return Application.Current?.TryFindResource("Brush.Primary") as Brush ?? Brushes.Orange;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
