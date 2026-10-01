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
        RecordHotkeyButton.Click += (_, _) => StartCapture();
        ResetHotkeyButton.Click += (_, _) => ApplyHotkey(Hotkey.Default());
    }

    // ---------- 热键录制 ----------

    private void StartCapture()
    {
        _capturing = true;
        // 挂起当前绑定，否则旧组合键会被系统拦截、无法重新录制同一键
        ((App)Application.Current).SuspendHotkey();
        HotkeyText.Text = "请按下新组合键…";
        HotkeyIdleHint.Visibility = Visibility.Collapsed;
        HotkeyErrorHint.Visibility = Visibility.Collapsed;
        RecordHotkeyButton.Visibility = Visibility.Collapsed;
        ResetHotkeyButton.Visibility = Visibility.Visible;
        Activate();
    }

    /// <summary>退出录制；未换绑时按设置恢复原绑定。</summary>
    private void StopCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        ((App)Application.Current).ResumeHotkey();
        RestoreCaptureUi();
    }

    private void RestoreCaptureUi()
    {
        HotkeyText.Text = Hotkey.Parse(_settings.Hotkey).ToString();
        HotkeyIdleHint.Visibility = Visibility.Visible;
        HotkeyErrorHint.Visibility = Visibility.Collapsed;
        RecordHotkeyButton.Visibility = Visibility.Visible;
        ResetHotkeyButton.Visibility = Visibility.Hidden;
    }

    private void ApplyHotkey(Hotkey hk)
    {
        if (((App)Application.Current).TryRebindHotkey(hk))
        {
            _capturing = false; // 新键已生效，无需恢复旧绑定
            RestoreCaptureUi();
            return;
        }
        HotkeyIdleHint.Visibility = Visibility.Collapsed;
        HotkeyErrorHint.Text = $"{hk} 注册失败：可能已被系统或其他程序占用，请换一组或按 Esc 取消";
        HotkeyErrorHint.Visibility = Visibility.Visible;
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
            HotkeyIdleHint.Visibility = Visibility.Collapsed;
            HotkeyErrorHint.Text = "至少包含一个修饰键（Alt / Ctrl / Shift / Win），或直接用 F1–F12";
            HotkeyErrorHint.Visibility = Visibility.Visible;
            return;
        }
        ApplyHotkey(new Hotkey(mods, key));
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
