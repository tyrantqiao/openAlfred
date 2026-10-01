using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using OpenAlfred.App.Interop;
using OpenAlfred.Core;
using OpenAlfred.Core.Clipboard;

namespace OpenAlfred.App.Services;

/// <summary>执行 QueryResult 携带的动作。</summary>
public sealed class ActionExecutor
{
    private readonly ClipboardStore _store;

    /// <summary>唤起窗口前台窗口句柄，用于剪贴板粘贴回写。</summary>
    public IntPtr PreviousWindow { get; set; }

    /// <summary>本应用写剪贴板前调用，供 ClipboardMonitor 设置防回环标记。</summary>
    public Action? BeforeClipboardWrite { get; set; }

    public ActionExecutor(ClipboardStore store) => _store = store;

    public void Execute(QueryResult result, bool secondary)
    {
        var action = secondary ? result.SecondaryAction : result.Action;
        var payload = secondary ? result.SecondaryPayload : result.Payload;
        if (action == ResultActionKind.None || payload is null) return;

        switch (action)
        {
            case ResultActionKind.CopyText:
                Try(() =>
                {
                    BeforeClipboardWrite?.Invoke();
                    Clipboard.SetDataObject(payload);
                });
                break;

            case ResultActionKind.OpenPath:
                Try(() =>
                {
                    Process.Start(new ProcessStartInfo(payload) { UseShellExecute = true });
                });
                break;

            case ResultActionKind.RevealInExplorer:
                Try(() =>
                {
                    var args = File.GetAttributes(payload).HasFlag(FileAttributes.Directory)
                        ? $"\"{payload}\""
                        : $"/select,\"{payload}\"";
                    Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
                });
                break;

            case ResultActionKind.PasteClipboard:
                PasteFromHistory(payload);
                break;
        }
    }

    /// <summary>把历史记录写回剪贴板，然后把焦点还给原窗口并模拟 Ctrl+V。</summary>
    private void PasteFromHistory(string entryId)
    {
        var entry = _store.Find(entryId);
        if (entry is null) return;

        BeforeClipboardWrite?.Invoke();
        if (entry.Kind == "image")
        {
            if (entry.ImagePath is null || !File.Exists(entry.ImagePath)) return;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(entry.ImagePath);
            image.EndInit();
            image.Freeze();
            Clipboard.SetImage(image);
        }
        else
        {
            Clipboard.SetDataObject(entry.Content);
        }

        HideRequested?.Invoke();

        if (PreviousWindow != IntPtr.Zero)
        {
            // 等窗口隐藏动画完成后再回焦，避免焦点竞争
            Task.Delay(120).ContinueWith(_ =>
            {
                NativeMethods.ShowWindow(PreviousWindow, NativeMethods.SW_RESTORE);
                if (NativeMethods.SetForegroundWindow(PreviousWindow))
                {
                    Thread.Sleep(50);
                    NativeMethods.SendCtrlV();
                }
            }, TaskScheduler.Default);
        }
    }

    public event Action? HideRequested;

    private static void Try(Action action)
    {
        try { action(); }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }
}
