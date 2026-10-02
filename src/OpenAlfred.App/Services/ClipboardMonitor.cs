using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using OpenAlfred.App.Interop;
using OpenAlfred.Core.Clipboard;

namespace OpenAlfred.App.Services;

/// <summary>
/// 剪贴板监听：挂接主窗口消息循环，捕获 WM_CLIPBOARDUPDATE，
/// 把文本 / 图片写入 ClipboardStore。自身写剪贴板时通过 IgnoreNextUpdate 防回环。
/// </summary>
public sealed class ClipboardMonitor
{
    private readonly ClipboardStore _store;
    private readonly string _imageDir;

    public bool Paused { get; set; }
    /// <summary>本应用主动写剪贴板后置 true，跳过下一次更新事件。</summary>
    public bool IgnoreNextUpdate { get; set; }

    public ClipboardMonitor(ClipboardStore store)
    {
        _store = store;
        _imageDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "openAlfred", "images");
    }

    public void Attach(IntPtr hwnd)
    {
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);
        // 必须向系统注册剪贴板监听，否则永远收不到 WM_CLIPBOARDUPDATE
        NativeMethods.AddClipboardFormatListener(hwnd);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_CLIPBOARDUPDATE) return 0;

        if (IgnoreNextUpdate)
        {
            IgnoreNextUpdate = false;
            return 0;
        }
        if (Paused) return 0;

        try
        {
            if (Clipboard.ContainsText())
                _store.AddText(Clipboard.GetText());
            else if (Clipboard.ContainsImage())
                SaveImage();
        }
        catch (Exception)
        {
            // 剪贴板被其他进程占用时短暂失败，忽略本次更新
        }
        return 0;
    }

    private void SaveImage()
    {
        if (Clipboard.GetImage() is not BitmapSource image) return;

        // 先编码到内存，计算内容 SHA256 用于重复识别，再落盘
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var buffer = new MemoryStream();
        encoder.Save(buffer);
        var bytes = buffer.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        Directory.CreateDirectory(_imageDir);
        var path = Path.Combine(_imageDir, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
        File.WriteAllBytes(path, bytes);
        _store.AddImage(path, hash);
    }
}
