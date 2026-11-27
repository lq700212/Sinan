using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace Sinan.App.Services;

/// <summary>
/// 二维码位图渲染：QRCoder 只产 PNG 字节，WPF 再按需解成冻结的 BitmapImage。
///
/// 用 PngByteQRCode 而不是 System.Drawing：现场机器不保证装了 GDI+ 编码器，
/// 纯托管 PNG 输出在 Windows 上更稳，也不需要额外原生依赖。
/// </summary>
public static class PairQrCode
{
    /// <summary>生成可直接绑定到 Image.Source 的位图（Freeze 后跨线程只读安全）。</summary>
    public static ImageSource Render(string payload, int pixelsPerModule = 8)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule);

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(png);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
