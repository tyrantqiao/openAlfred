using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenAlfred.App.Services;
using Wpf.Ui.Controls;

namespace OpenAlfred.App;

/// <summary>设置窗口：主题、唤起热键、失焦隐藏、自启、剪贴板。</summary>
public partial class SettingsWindow : FluentWindow
{
    private readonly AppSettings _settings = App.Settings;
    private bool _capturing;
    private CaptureTarget _captureTarget;

    public SettingsWindow()
    {
        InitializeComponent();

        ThemeBox.Items.Add("跟随系统");
        ThemeBox.Items.Add("浅色");
        ThemeBox.Items.Add("深色");
        ThemeBox.SelectedIndex = (int)_settings.Theme;
        ThemeBox.SelectionChanged += (_, _) =>
        {
            _settings.Theme = (ThemeMode)ThemeBox.SelectedIndex;
            _settings.Save();
            ThemeService.Apply(_settings.Theme);
        };

        HideOnLostFocusBox.IsChecked = _settings.HideOnLostFocus;
        HideOnLostFocusBox.Checked += (_, _) => SetAndSave(() => _settings.HideOnLostFocus = true);
        HideOnLostFocusBox.Unchecked += (_, _) => SetAndSave(() => _settings.HideOnLostFocus = false);

        MinimizeToTrayBox.IsChecked = _settings.MinimizeToTray;
        MinimizeToTrayBox.Checked += (_, _) => SetAndSave(() => _settings.MinimizeToTray = true);
        MinimizeToTrayBox.Unchecked += (_, _) => SetAndSave(() => _settings.MinimizeToTray = false);

        StartMinimizedBox.IsChecked = _settings.StartMinimized;
        StartMinimizedBox.Checked += (_, _) => SetAndSave(() => _settings.StartMinimized = true);
        StartMinimizedBox.Unchecked += (_, _) => SetAndSave(() => _settings.StartMinimized = false);

        TrayTipBox.IsChecked = _settings.TrayTipEnabled;
        TrayTipBox.Checked += (_, _) => SetAndSave(() => _settings.TrayTipEnabled = true);
        TrayTipBox.Unchecked += (_, _) => SetAndSave(() => _settings.TrayTipEnabled = false);

        StartupBox.IsChecked = _settings.RunAtStartup;
        StartupBox.Checked += (_, _) => SetAndSave(() => _settings.RunAtStartup = true, applyStartup: true);
        StartupBox.Unchecked += (_, _) => SetAndSave(() => _settings.RunAtStartup = false, applyStartup: true);

        ClipboardPauseBox.IsChecked = _settings.ClipboardPaused;
        ClipboardPauseBox.Checked += (_, _) => SetAndSave(() =>
        {
            _settings.ClipboardPaused = true;
            App.ClipboardMonitor.Paused = true;
        });
        ClipboardPauseBox.Unchecked += (_, _) => SetAndSave(() =>
        {
            _settings.ClipboardPaused = false;
            App.ClipboardMonitor.Paused = false;
        });

        ClearClipboardButton.Click += (_, _) => App.ClipboardStore.Clear();

        HotkeyText.Text = Hotkey.Parse(_settings.Hotkey).ToString();
        RecordHotkeyButton.Click += (_, _) => StartCapture(CaptureTarget.Launch);
        ResetHotkeyButton.Click += (_, _) => ApplyHotkey(CaptureTarget.Launch, Hotkey.Default());

        ClipHotkeyText.Text = Hotkey.Parse(_settings.ClipboardHotkey).ToString();
        ClipRecordButton.Click += (_, _) => StartCapture(CaptureTarget.Clipboard);
        ClipResetButton.Click += (_, _) => ApplyHotkey(CaptureTarget.Clipboard, Hotkey.DefaultClipboard());
    }

    // ---------- 热键录制 ----------

    private enum CaptureTarget { Launch, Clipboard }

    // 控件在 XAML 中的类型：标签/提示为 System.Windows.Controls.TextBlock，按钮为 Wpf.Ui.Controls.Button
    private System.Windows.Controls.TextBlock HotkeyLabelOf(CaptureTarget t) => t == CaptureTarget.Launch ? HotkeyText : ClipHotkeyText;
    private Wpf.Ui.Controls.Button RecordButtonOf(CaptureTarget t) => t == CaptureTarget.Launch ? RecordHotkeyButton : ClipRecordButton;
    private Wpf.Ui.Controls.Button ResetButtonOf(CaptureTarget t) => t == CaptureTarget.Launch ? ResetHotkeyButton : ClipResetButton;
    private System.Windows.Controls.TextBlock IdleHintOf(CaptureTarget t) => t == CaptureTarget.Launch ? HotkeyIdleHint : ClipIdleHint;
    private System.Windows.Controls.TextBlock ErrorHintOf(CaptureTarget t) => t == CaptureTarget.Launch ? HotkeyErrorHint : ClipErrorHint;

    private void StartCapture(CaptureTarget target)
    {
        _capturing = true;
        _captureTarget = target;
        // 挂起当前绑定，否则旧组合键会被系统拦截、无法重新录制同一键
        if (target == CaptureTarget.Clipboard) ((App)Application.Current).SuspendClipboardHotkey();
        else ((App)Application.Current).SuspendHotkey();
        HotkeyLabelOf(target).Text = "请按下新组合键…";
        IdleHintOf(target).Visibility = Visibility.Collapsed;
        ErrorHintOf(target).Visibility = Visibility.Collapsed;
        RecordButtonOf(target).Visibility = Visibility.Collapsed;
        ResetButtonOf(target).Visibility = Visibility.Visible;
        Activate();
    }

    /// <summary>退出录制；未换绑时按设置恢复原绑定。</summary>
    private void StopCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        if (_captureTarget == CaptureTarget.Clipboard) ((App)Application.Current).ResumeClipboardHotkey();
        else ((App)Application.Current).ResumeHotkey();
        RestoreCaptureUi(_captureTarget);
    }

    private void RestoreCaptureUi(CaptureTarget target)
    {
        var str = target == CaptureTarget.Clipboard
            ? Hotkey.Parse(_settings.ClipboardHotkey).ToString()
            : Hotkey.Parse(_settings.Hotkey).ToString();
        HotkeyLabelOf(target).Text = str;
        IdleHintOf(target).Visibility = Visibility.Visible;
        ErrorHintOf(target).Visibility = Visibility.Collapsed;
        RecordButtonOf(target).Visibility = Visibility.Visible;
        ResetButtonOf(target).Visibility = Visibility.Hidden;
    }

    private void ApplyHotkey(CaptureTarget target, Hotkey hk)
    {
        var app = (App)Application.Current;
        bool ok = target == CaptureTarget.Clipboard ? app.TryRebindClipboardHotkey(hk) : app.TryRebindHotkey(hk);
        if (ok)
        {
            _capturing = false; // 新键已生效，无需恢复旧绑定
            RestoreCaptureUi(target);
            return;
        }
        IdleHintOf(target).Visibility = Visibility.Collapsed;
        ErrorHintOf(target).Text = $"{hk} 注册失败：可能已被系统或其他程序占用，请换一组或按 Esc 取消";
        ErrorHintOf(target).Visibility = Visibility.Visible;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (!_capturing) return;
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return; // 只按下修饰键，继续等待主键

        if (key == Key.Escape)
        {
            StopCapture();
            return;
        }

        var mods = Keyboard.Modifiers;
        bool functionKey = key is >= Key.F1 and <= Key.F12;
        if (mods == ModifierKeys.None && !functionKey)
        {
            IdleHintOf(_captureTarget).Visibility = Visibility.Collapsed;
            ErrorHintOf(_captureTarget).Text = "至少包含一个修饰键（Alt / Ctrl / Shift / Win），或直接用 F1–F12";
            ErrorHintOf(_captureTarget).Visibility = Visibility.Visible;
            return;
        }
        ApplyHotkey(_captureTarget, new Hotkey(mods, key));
    }

    private void SetAndSave(Action mutate, bool applyStartup = false)
    {
        mutate();
        _settings.Save();
        if (applyStartup) App.ApplyStartupSetting(_settings.RunAtStartup);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        // 录制中失焦：先退出录制恢复热键绑定，再隐藏
        StopCapture();
        // 设置页失焦即关闭，方便 Esc/点击外部快速回到主流程
        Hide();
    }
}
