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

    /// <summary>执行动作；返回是否成功（用于表现层给出拷贝成功/失败反馈）。</summary>
    public bool Execute(QueryResult result, bool secondary)
    {
        var action = secondary ? result.SecondaryAction : result.Action;
        var payload = secondary ? result.SecondaryPayload : result.Payload;
        if (action == ResultActionKind.None || payload is null) return false;

        switch (action)
        {
            case ResultActionKind.CopyText:
                return Try(() =>
                {
                    BeforeClipboardWrite?.Invoke();
                    Clipboard.SetDataObject(payload);
                });

            case ResultActionKind.OpenPath:
                return Try(() =>
                {
                    Process.Start(new ProcessStartInfo(payload) { UseShellExecute = true });
                });

            case ResultActionKind.RevealInExplorer:
                return Try(() =>
                {
                    var args = File.GetAttributes(payload).HasFlag(FileAttributes.Directory)
                        ? $"\"{payload}\""
                        : $"/select,\"{payload}\"";
                    Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
                });

            case ResultActionKind.PasteClipboard:
                PasteFromHistory(payload);
                return true;

            case ResultActionKind.CopyClipboardEntry:
                return CopyFromHistory(payload);
        }
        return false;
    }

    /// <summary>把历史记录内容拷贝到剪贴板（不粘贴、不隐藏窗口）；成功返回 true。</summary>
    private bool CopyFromHistory(string entryId)
    {
        var entry = _store.Find(entryId);
        if (entry is null) return false;
        BeforeClipboardWrite?.Invoke();
        var ok = false;
        Try(() => ok = WriteEntryToClipboard(entry));
        return ok;
    }

    /// <summary>把历史记录写回剪贴板，然后把焦点还给原窗口并模拟 Ctrl+V。</summary>
    private void PasteFromHistory(string entryId)
    {
        var entry = _store.Find(entryId);
        if (entry is null) return;

        BeforeClipboardWrite?.Invoke();
        if (!WriteEntryToClipboard(entry)) return;

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

    /// <summary>把一条记录写回系统剪贴板；成功返回 true。调用前应已置 IgnoreNextUpdate。</summary>
    private bool WriteEntryToClipboard(ClipboardEntry entry)
    {
        if (entry.Kind == "image")
        {
            if (entry.ImagePath is null || !File.Exists(entry.ImagePath)) return false;
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
        return true;
    }

    public event Action? HideRequested;

    private static bool Try(Action action)
    {
        try { action(); return true; }
        catch (Exception ex) { Debug.WriteLine(ex); return false; }
    }
}
