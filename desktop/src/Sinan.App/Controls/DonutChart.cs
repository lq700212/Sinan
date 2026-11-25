using System;
using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Sinan.App.Controls;

/// <summary>
/// 极简环形图（设计稿 .donut）：只负责把一组数值画成等宽圆环段。
///
/// 为什么自绘：WPF 没有 CSS conic-gradient，也没有内置环形图；引第三方图表库会为
/// 一个 170px 的图多背一个依赖。这里只实现设计稿需要的"按值分段 + 段间留白"，
/// 中心文字由页面用 TextBlock 叠在上面。
///
/// 数据项约定（弱类型，避免 ViewModel 引用 WPF 类型）：
/// - <c>Value</c>：double / int，段的数值（0 的段自动跳过）；
/// - <c>PaletteKey</c>：主题 Brush 的 key（如 <c>Brush.Primary</c>），找不到时退主色。
/// </summary>
public sealed class DonutChart : FrameworkElement
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(IEnumerable), typeof(DonutChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>环宽（像素）。设计稿 donut 环约 18–20px。</summary>
    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(DonutChart),
        new FrameworkPropertyMetadata(18d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>相邻段之间的空白角度；设计稿用细白缝区分占比。</summary>
    public static readonly DependencyProperty GapDegreesProperty = DependencyProperty.Register(
        nameof(GapDegrees), typeof(double), typeof(DonutChart),
        new FrameworkPropertyMetadata(1.6d, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable? Segments
    {
        get => (IEnumerable?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public double GapDegrees
    {
        get => (double)GetValue(GapDegreesProperty);
        set => SetValue(GapDegreesProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 2 || h <= 2 || Segments == null) return;

        double size = Math.Min(w, h);
        double thickness = Math.Max(1, Math.Min(Thickness, size / 2 - 1));
        double radius = (size - thickness) / 2;
        var center = new Point(w / 2, h / 2);

        var items = new System.Collections.Generic.List<(double Value, Brush Brush)>();
        double total = 0;
        foreach (var item in Segments)
        {
            if (item == null) continue;
            var type = item.GetType();
            double value = ReadDouble(type, item, "Value");
            if (value <= 0) continue;
            string? key = type.GetProperty("PaletteKey")?.GetValue(item) as string;
            var brush = (key == null ? null : TryFindResource(key) as Brush)
                        ?? TryFindResource("Brush.Primary") as Brush
                        ?? Brushes.Orange;
            items.Add((value, brush));
            total += value;
        }

        if (total <= 0)
        {
            // 全 0 时画一圈空槽，避免中心文字悬空。
            var empty = TryFindResource("Brush.CardWell") as Brush ?? Brushes.Gainsboro;
            dc.DrawEllipse(null, new Pen(empty, thickness), center, radius, radius);
            return;
        }

        double start = -90; // 12 点方向顺时针
        foreach (var (value, brush) in items)
        {
            double sweep = 360 * value / total;
            double drawn = Math.Max(0.4, sweep - GapDegrees);
            var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };

            if (drawn >= 359.4)
            {
                dc.DrawEllipse(null, pen, center, radius, radius);
            }
            else
            {
                var geometry = BuildArc(center, radius, start, drawn);
                dc.DrawGeometry(null, pen, geometry);
            }

            start += sweep;
        }
    }

    private static double ReadDouble(Type type, object item, string property)
    {
        object? raw = type.GetProperty(property)?.GetValue(item);
        try
        {
            return raw == null ? 0 : Convert.ToDouble(raw, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0;
        }
    }

    private static StreamGeometry BuildArc(Point center, double radius, double startDegrees, double sweepDegrees)
    {
        var start = PointOn(center, radius, startDegrees);
        var end = PointOn(center, radius, startDegrees + sweepDegrees);
        bool largeArc = sweepDegrees > 180;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(start, isFilled: false, isClosed: false);
            ctx.ArcTo(end, new Size(radius, radius), 0, largeArc,
                SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Point PointOn(Point center, double radius, double degrees)
    {
        double rad = degrees * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
    }
}
