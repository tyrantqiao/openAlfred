using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OpenAlfred.App.Interop;
using OpenAlfred.App.Services;
using OpenAlfred.Core;
using Wpf.Ui.Controls;
using TextBox = Wpf.Ui.Controls.TextBox;
using TextBlock = System.Windows.Controls.TextBlock;

namespace OpenAlfred.App;

/// <summary>
/// 主窗口：单搜索框 + 结果列表，全局热键唤起（默认 Alt+Space，可在设置页修改），键盘全操作。
/// </summary>
public partial class MainWindow : FluentWindow
{
    private const int HotkeyId = 0xA1F0;
    private const int HotkeyIdAlt = 0xA1F1;
    private const int ClipHotkeyId = 0xA1F2;
    private const int ClipHotkeyIdAlt = 0xA1F3;

    private readonly QueryRouter _router = App.Router;
    private readonly DispatcherTimer _debounce;
    private CancellationTokenSource? _queryCts;
    private bool _suppressHideOnLostFocus;
    private bool _clipboardMode;
    private IntPtr _hwnd;
    private int _hotkeyId = HotkeyId;
    private Hotkey? _activeHotkey;
    private int _clipHotkeyId = ClipHotkeyId;
    private Hotkey? _activeClipHotkey;
    // 底部临时状态提示（如“已拷贝”）闪现后恢复原文本
    private DispatcherTimer? _statusTimer;
    private bool _statusFlashing;
    private string? _savedFooterText;
    private Brush? _savedFooterBrush;

    public MainWindow()
    {
        InitializeComponent();
        BuildHintPills();

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _ = RunQueryAsync();
        };

        App.FileIndex.ProgressChanged += OnIndexProgress;
        Loaded += (_, _) => PositionWindow();
        SourceInitialized += OnSourceInitialized;
        Deactivated += OnDeactivated;
        // 窗口级隧道按键：无论焦点在搜索框还是结果列表，↑↓/Enter/Esc 等都先由窗口统一处理
        PreviewKeyDown += Window_PreviewKeyDown;
    }

    // ---------- 唤起与快捷键 ----------

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(WndProc);

        var hk = Hotkey.Parse(App.Settings.Hotkey);
        if (!ApplyHotkey(hk))
        {
            FooterRight.Text = $"{hk} 被占用，请通过托盘唤起";
        }

        // 剪贴板历史直达热键（默认 Ctrl+Alt+V），注册失败不阻断主唤起
        ApplyClipboardHotkey(Hotkey.Parse(App.Settings.ClipboardHotkey));

        App.ClipboardMonitor.Attach(_hwnd);
        App.Executor.BeforeClipboardWrite ??= () => App.ClipboardMonitor.IgnoreNextUpdate = true;
    }

    /// <summary>
    /// 重新绑定全局热键：先注册到新 id，成功才注销旧的，失败则保留原绑定。
    /// </summary>
    public bool ApplyHotkey(Hotkey hk)
    {
        if (_hwnd == IntPtr.Zero) return false;

        uint mods = hk.ToWin32Modifiers();
        uint vk = hk.ToVirtualKey();

        // 首次注册：直接用主 id
        if (_activeHotkey is null)
        {
            if (!NativeMethods.RegisterHotKey(_hwnd, HotkeyId, mods, vk)) return false;
            _hotkeyId = HotkeyId;
            _activeHotkey = hk;
            return true;
        }

        if (_activeHotkey == hk) return true;

        // 换绑：用备用 id 探测，避免新键冲突时丢失旧绑定
        int probe = _hotkeyId == HotkeyId ? HotkeyIdAlt : HotkeyId;
        if (!NativeMethods.RegisterHotKey(_hwnd, probe, mods, vk)) return false;
        NativeMethods.UnregisterHotKey(_hwnd, _hotkeyId);
        _hotkeyId = probe;
        _activeHotkey = hk;
        return true;
    }

    /// <summary>注销当前热键（设置页录制期间释放占用，退出录制后恢复）。</summary>
    public void SuspendHotkey()
    {
        if (_activeHotkey is null || _hwnd == IntPtr.Zero) return;
        NativeMethods.UnregisterHotKey(_hwnd, _hotkeyId);
        _activeHotkey = null;
    }

    /// <summary>注册/换绑剪贴板历史直达热键，逻辑与主热键一致（备用 id 探测避免丢绑定）。</summary>
    public bool ApplyClipboardHotkey(Hotkey hk)
    {
        if (_hwnd == IntPtr.Zero) return false;

        uint mods = hk.ToWin32Modifiers();
        uint vk = hk.ToVirtualKey();

        if (_activeClipHotkey is null)
        {
            if (!NativeMethods.RegisterHotKey(_hwnd, ClipHotkeyId, mods, vk)) return false;
            _clipHotkeyId = ClipHotkeyId;
            _activeClipHotkey = hk;
            return true;
        }

        if (_activeClipHotkey == hk) return true;

        int probe = _clipHotkeyId == ClipHotkeyId ? ClipHotkeyIdAlt : ClipHotkeyId;
        if (!NativeMethods.RegisterHotKey(_hwnd, probe, mods, vk)) return false;
        NativeMethods.UnregisterHotKey(_hwnd, _clipHotkeyId);
        _clipHotkeyId = probe;
        _activeClipHotkey = hk;
        return true;
    }

    /// <summary>挂起剪贴板直达热键（录制新键时释放占用）。</summary>
    public void SuspendClipboardHotkey()
    {
        if (_activeClipHotkey is null || _hwnd == IntPtr.Zero) return;
        NativeMethods.UnregisterHotKey(_hwnd, _clipHotkeyId);
        _activeClipHotkey = null;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (id != _hotkeyId && id != _clipHotkeyId) return 0;

            // 唤起失败不能炸掉常驻进程，异常在消息层面兜住
            try
            {
                if (id == _clipHotkeyId) ShowClipboardHistory();
                else Toggle();
            }
            catch (Exception ex) { FooterRight.Text = $"唤起失败：{ex.Message}"; }
            handled = true;
        }
        return 0;
    }

    public void Toggle()
    {
        if (IsVisible && IsActive) HideWindow();
        else ShowWindow();
    }

    /// <summary>直达剪贴板历史：唤起窗口并预填 >clip 命令，列出全部历史；已在前台时收起。</summary>
    public void ShowClipboardHistory()
    {
        if (IsVisible && IsActive && _clipboardMode)
        {
            HideWindow();
            return;
        }
        _clipboardMode = true;
        // selectAll:false —— 不选中 “>clip”，光标落在末尾，直接打字即过滤；↑↓ 由窗口级按键导航列表
        ShowWindow(selectAll: false);
        SearchBox.Text = ">clip ";
        SearchBox.CaretIndex = SearchBox.Text.Length;
        SearchBox.Focus();
        _ = RunQueryAsync();
    }

    public void ShowWindow(bool selectAll = true)
    {
        if (!selectAll) _clipboardMode = true;
        else _clipboardMode = false;

        // 系统最小化状态先恢复再唤起
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

        // 记录唤起前的前台窗口，供剪贴板粘贴回写
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground != new WindowInteropHelper(this).Handle)
            App.Executor.PreviousWindow = foreground;

        Show();
        AnimateIn();
        Activate();
        SearchBox.Focus();
        Dispatcher.BeginInvoke(() =>
        {
            if (selectAll) SearchBox.SelectAll();
            _ = RunQueryAsync();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    public void HideWindow()
    {
        Hide();
        ResultsList.ItemsSource = null;
        // 复位模式与文本，避免下次普通唤起残留 ">clip"
        _clipboardMode = false;
        SearchBox.Text = "";
    }

    /// <summary>最小化按钮：默认收进托盘常驻后台；关闭该设置后用系统最小化。</summary>
    private void MinButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.Settings.MinimizeToTray)
        {
            HideWindow();
            App.NotifyMinimizedToTray();
        }
        else
        {
            WindowState = WindowState.Minimized;
        }
    }

    /// <summary>关闭主窗口 = 收进托盘（进程常驻），退出只能走托盘菜单。</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        if (App.Settings.MinimizeToTray)
        {
            HideWindow();
            App.NotifyMinimizedToTray();
        }
        else
        {
            WindowState = WindowState.Minimized;
        }
    }

    /// <summary>
    /// 出现动画作用于内容根 Border：WPF 禁止对未开 AllowsTransparency 的 Window 设 RenderTransform，
    /// 否则每次唤起都会抛异常直接带走常驻进程。
    /// </summary>
    private void AnimateIn()
    {
        RootBorder.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

        if (RootBorder.RenderTransform is not ScaleTransform scale)
        {
            scale = new ScaleTransform(1, 1);
            RootBorder.RenderTransformOrigin = new Point(0.5, 0);
            RootBorder.RenderTransform = scale;
        }
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(120))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(120))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void PositionWindow()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + workArea.Height / 8;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!App.Settings.HideOnLostFocus || _suppressHideOnLostFocus) return;

        // 焦点转到本应用自己的窗口（如设置页）时不隐藏
        var foreground = NativeMethods.GetForegroundWindow();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (foreground == hwnd) return;

        HideWindow();
    }

    // ---------- 查询 ----------

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private async Task RunQueryAsync()
    {
        _queryCts?.Cancel();
        _queryCts = new CancellationTokenSource();
        var text = SearchBox.Text;

        IReadOnlyList<QueryResult> results;
        try
        {
            results = await _router.RouteAsync(text, _queryCts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_queryCts.IsCancellationRequested) return;

        var hasQuery = !string.IsNullOrWhiteSpace(text);
        ResultsList.ItemsSource = results;
        ResultsList.Visibility = hasQuery && results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        QueryDivider.Visibility = hasQuery ? Visibility.Visible : Visibility.Collapsed;
        HintBar.Visibility = hasQuery ? Visibility.Collapsed : Visibility.Visible;

        // clip 直达模式：底部提示切换为剪贴板历史可用操作
        FooterLeft.Text = hasQuery && QueryRouter.Parse(text).Keyword == "clip"
            ? "↑↓ 选择 · Enter 粘贴 · Ctrl+C 拷贝 · Ctrl+Backspace 删除"
            : "↑↓ 选择 · Enter 执行 · Tab 补全";

        if (results.Count > 0)
        {
            ResultsList.SelectedIndex = 0;
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
        }

        if (hasQuery && results.Count == 0)
        {
            var emptyTitle = QueryRouter.Parse(text).Keyword == "clip"
                ? "剪贴板历史为空 · 复制的内容会出现在这里"
                : "没有匹配的结果";
            ResultsList.ItemsSource = new[]
            {
                new QueryResult { Title = emptyTitle, Source = "none", Score = -1 },
            };
            ResultsList.SelectedIndex = -1;
            ResultsList.Visibility = Visibility.Visible;
        }
    }

    // ---------- 键盘 ----------

    /// <summary>
    /// 窗口级 PreviewKeyDown（隧道事件，先于搜索框/列表）：统一处理导航与执行，
    /// 保证焦点在搜索框时 ↑↓ 依然能移动列表选中项；可打印字符不拦截，继续冒泡到搜索框输入。
    /// </summary>
    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                // 剪贴板历史模式：Esc 直接退出；普通模式：先清空文本，再按一次才退出
                if (_clipboardMode || SearchBox.Text.Length == 0) HideWindow();
                else SearchBox.Text = "";
                e.Handled = true;
                break;

            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;

            case Key.Down:
                MoveSelection(1);
                e.Handled = true;
                break;

            case Key.Enter when Keyboard.Modifiers == ModifierKeys.Control:
                await ExecuteSelectedAsync(secondary: true);
                e.Handled = true;
                break;

            case Key.Enter:
                await ExecuteSelectedAsync(secondary: false);
                e.Handled = true;
                break;

            case Key.Tab:
                CompleteKeyword();
                e.Handled = true;
                break;

            case Key.C when Keyboard.Modifiers == ModifierKeys.Control:
                CopySelectedPayload();
                e.Handled = true;
                break;

            case Key.Back when Keyboard.Modifiers == ModifierKeys.Control:
                DeleteSelectedClipboardEntry();
                e.Handled = true;
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        var count = ResultsList.Items.Count;
        if (count == 0) return;
        var next = (ResultsList.SelectedIndex + delta + count) % count;
        ResultsList.SelectedIndex = next;
        ResultsList.ScrollIntoView(ResultsList.Items[next]);
    }

    private async Task ExecuteSelectedAsync(bool secondary)
    {
        if (ResultsList.SelectedItem is not QueryResult result) return;

        var effectiveAction = secondary ? result.SecondaryAction : result.Action;

        // 拷贝类动作：保持窗口打开并提示成功，方便连续操作（与 Ctrl+C 行为一致）
        if (effectiveAction is ResultActionKind.CopyClipboardEntry or ResultActionKind.CopyText)
        {
            var ok = App.Executor.Execute(result, secondary);
            ShowStatus(ok ? "✓ 已拷贝到剪贴板" : "拷贝失败");
            if (ok) FlashCopiedRow();
            return;
        }

        _suppressHideOnLostFocus = true;
        App.Executor.Execute(result, secondary);

        // 打开/粘贴类以及次要动作收起窗口；其余留在窗口内刷新
        if (result.Action is ResultActionKind.OpenPath or ResultActionKind.PasteClipboard
            || secondary)
        {
            HideWindow();
        }
        else
        {
            await RunQueryAsync();
        }
        _suppressHideOnLostFocus = false;
    }

    private void CopySelectedPayload()
    {
        if (ResultsList.SelectedItem is not QueryResult { Payload: not null } result) return;
        // 剪贴板条目的 Payload 是记录 Id，需走专用动作拷贝其真实内容；其余项直接拷贝 Payload
        var action = result.Source == "clip" ? ResultActionKind.CopyClipboardEntry : ResultActionKind.CopyText;
        var ok = App.Executor.Execute(result with { Action = action }, secondary: false);
        ShowStatus(ok ? "✓ 已拷贝到剪贴板" : "拷贝失败");
        if (ok) FlashCopiedRow();
    }

    /// <summary>对当前选中行施加“已拷贝”特效：强调色底衬闪现后渐隐。</summary>
    private void FlashCopiedRow()
    {
        var index = ResultsList.SelectedIndex;
        if (index < 0) return;
        if (ResultsList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container) return;
        if (container.Template?.FindName("CopyFlash", container) is not Border flash) return;

        var anim = new DoubleAnimationUsingKeyFrames();
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(0.5, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
        // 先清零残留动画再重启，保证连续拷贝每次都能看到闪现
        flash.BeginAnimation(UIElement.OpacityProperty, null);
        flash.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    /// <summary>在底部右侧提示区闪现一条状态消息（强调色），约 1.6s 后恢复原文本。</summary>
    private void ShowStatus(string message)
    {
        // 未在闪现中才快照原始文本/颜色，避免连续拷贝把上一轮的“原样”覆盖掉
        if (!_statusFlashing)
        {
            _savedFooterText = FooterRight.Text;
            _savedFooterBrush = FooterRight.Foreground;
        }
        _statusFlashing = true;
        FooterRight.Text = message;
        var brushKey = message.StartsWith('✓') ? "OaAccentBrush" : "OaTextSecondaryBrush";
        FooterRight.Foreground = (Brush)FindResource(brushKey);

        _statusTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        _statusTimer.Stop();
        _statusTimer.Tick -= OnStatusTimerTick;
        _statusTimer.Tick += OnStatusTimerTick;
        _statusTimer.Start();
    }

    private void OnStatusTimerTick(object? sender, EventArgs e)
    {
        _statusTimer?.Stop();
        _statusFlashing = false;
        if (_savedFooterText is not null) FooterRight.Text = _savedFooterText;
        if (_savedFooterBrush is not null) FooterRight.Foreground = _savedFooterBrush;
    }

    private void DeleteSelectedClipboardEntry()
    {
        if (ResultsList.SelectedItem is QueryResult { Source: "clip", Payload: not null } result)
        {
            App.ClipboardStore.Remove(result.Payload);
            _ = RunQueryAsync();
        }
    }

    private void CompleteKeyword()
    {
        var text = SearchBox.Text.TrimStart();
        if (!text.StartsWith('>')) return;
        text = text[1..].TrimStart();
        var match = QueryRouter.Keywords
            .FirstOrDefault(k => k.StartsWith(text, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            SearchBox.Text = ">" + match + " ";
            SearchBox.CaretIndex = SearchBox.Text.Length;
        }
    }

    // ---------- 其他 ----------

    private void ResultsList_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is QueryResult)
            _ = ExecuteSelectedAsync(secondary: e.RightButton == MouseButtonState.Pressed);
    }

    private void OnIndexProgress(object? sender, int percent)
    {
        Dispatcher.BeginInvoke(() =>
        {
            FooterLeft.Text = percent < 0
                ? $"索引就绪 · {App.FileIndex.Count} 个文件"
                : $"文件索引建立中 {percent}%…";
        });
    }

    private void BuildHintPills()
    {
        (string keyword, string label)[] pills =
        [
            ("1+2", "直接输入算式即计算"),
            (">app", "启动应用"),
            (">clip", "剪贴板历史"),
            (">file", "检索文件"),
            (">json", "JSON 转换"),
            (">time", "时间查询"),
        ];

        foreach (var (keyword, label) in pills)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock
            {
                Text = keyword,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("OaAccentBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            panel.Children.Add(new TextBlock
            {
                Text = " " + label,
                FontSize = 12.5,
                Foreground = (Brush)FindResource("OaPillTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            });

            PillRow.Children.Add(new Border
            {
                Style = (Style)FindResource("OaPillStyle"),
                Child = panel,
            });
        }
    }
}
