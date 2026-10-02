using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace OpenAlfred.App.Converters;

/// <summary>
/// 图片绝对路径 → 缩略图 BitmapImage。按宽度限制解码，降低内存与 UI 线程开销；
/// 路径为空 / 文件不存在时返回 null（绑定端不显示）。
/// </summary>
public sealed class PathToBitmapImageConverter : IValueConverter
{
    public int DecodePixelWidth { get; init; } = 64;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path)) return null;
        try
        {
            if (!File.Exists(path)) return null;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // 立即读取，释放文件句柄
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.DecodePixelWidth = DecodePixelWidth;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception)
        {
            return null; // 损坏或非图片文件不影响列表其余项
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
