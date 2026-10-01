using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Hardcodet.Wpf.TaskbarNotification;
using OpenAlfred.App.Services;
using OpenAlfred.Core;
using OpenAlfred.Core.Apps;
using OpenAlfred.Core.Clipboard;
using OpenAlfred.Core.Files;
using IQueryProvider = OpenAlfred.Core.IQueryProvider;
using TimeProvider = OpenAlfred.Core.TimeProvider;
using Application = System.Windows.Application;

namespace OpenAlfred.App;

/// <summary>
/// 应用组合根：单实例、服务装配、托盘、主题。
/// </summary>
public partial class App : Application
{
    private const string AppId = "openAlfred-SingleInstance-Mutex";
    private const string ActivateEventName = "openAlfred-Activate-Event";

    private static Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private RegisteredWaitHandle? _activateWait;
    private bool _trayTipShown;
    private TaskbarIcon? _tray;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;

    public static AppSettings Settings { get; private set; } = null!;
    public static AppIndex AppIndex { get; private set; } = null!;
    public static ClipboardStore ClipboardStore { get; private set; } = null!;
    public static ClipboardMonitor ClipboardMonitor { get; private set; } = null!;
    public static FileIndex FileIndex { get; private set; } = null!;
    public static QueryRouter Router { get; private set; } = null!;
    public static ActionExecutor Executor { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 常驻后台进程：未处理异常不退出，避免唤起/查询失败导致整个应用消失
        DispatcherUnhandledException += (_, args) => args.Handled = true;

        _mutex = new Mutex(true, AppId, out bool createdNew);
        if (!createdNew)
        {
            // 已在后台运行：唤起已有实例后退出
            if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var existing))
            {
                using (existing) existing.Set();
            }
            Shutdown();
            return;
        }
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName, out _);
        _activateWait = ThreadPool.RegisterWaitForSingleObject(_activateEvent,
            (_, timedOut) =>
            {
                if (!timedOut) Dispatcher.BeginInvoke(() => _mainWindow?.ShowWindow());
            },
            null, Timeout.Infinite, false);

        Settings = AppSettings.Load();
        AppIndex = new AppIndex();
        AppIndex.Build(StartMenuRoots());
        ClipboardStore = new ClipboardStore();
        ClipboardMonitor = new ClipboardMonitor(ClipboardStore) { Paused = Settings.ClipboardPaused };
        FileIndex = new FileIndex();
        Router = new QueryRouter(new IQueryProvider[]
        {
            new CalculatorProvider(),
            new JsonProvider(),
            new TimeProvider(),
            new AppProvider(AppIndex),
            new ClipboardProvider(ClipboardStore),
            new FileProvider(FileIndex),
        });
        Executor = new ActionExecutor(ClipboardStore)
        {
            BeforeClipboardWrite = () => ClipboardMonitor.IgnoreNextUpdate = true,
        };
        Executor.HideRequested += () => _mainWindow?.HideWindow();

        ThemeService.Apply(Settings.Theme);
        ApplyStartupSetting(Settings.RunAtStartup);

        // 索引构建不依赖窗口显示，启动即后台扫描（支持启动即最小化）
        FileIndex.StartBuild(Settings.ResolveIndexRoots());

        _mainWindow = new MainWindow();
        if (Settings.StartMinimized)
        {
            // 不 Show，仅创建 HWND 以触发 SourceInitialized（注册全局热键与剪贴板钩子）
            new WindowInteropHelper(_mainWindow).EnsureHandle();
        }
        else
        {
            _mainWindow.Show();
        }

        CreateTrayIcon();
    }

    /// <summary>收进托盘时提示一次（每次运行期间只弹一次，避免打扰）。</summary>
    public static void NotifyMinimizedToTray()
    {
        if (Current is not App app || app._tray is null) return;
        if (!App.Settings.TrayTipEnabled || app._trayTipShown) return;
        app._trayTipShown = true;
        app._tray.ShowBalloonTip("openAlfred", $"已常驻后台 · {App.Settings.Hotkey} 唤起", BalloonIcon.None);
    }

    private void CreateTrayIcon()
    {
        _tray = new TaskbarIcon
        {
            IconSource = (System.Windows.Media.ImageSource)Resources["TrayIcon"],
            ToolTipText = $"openAlfred · {Settings.Hotkey} 唤起",
            ContextMenu = BuildTrayMenu(),
        };
        _tray.TrayLeftMouseDown += (_, _) => _mainWindow?.Toggle();
    }

    private ContextMenu BuildTrayMenu()
    {
        var menu = new ContextMenu();

        var openItem = new MenuItem { Header = "打开 openAlfred" };
        openItem.Click += (_, _) => _mainWindow?.ShowWindow();

        var settingsItem = new MenuItem { Header = "设置…" };
        settingsItem.Click += (_, _) => ShowSettings();

        var clearItem = new MenuItem { Header = "清除剪贴板历史" };
        clearItem.Click += (_, _) => ClipboardStore.Clear();

        var exitItem = new MenuItem { Header = "退出" };
        exitItem.Click += (_, _) => Shutdown();

        menu.Items.Add(openItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(clearItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);
        return menu;
    }

    /// <summary>设置页改键入口：重新绑定全局热键，成功后持久化并刷新托盘提示。</summary>
    public bool TryRebindHotkey(Hotkey hk)
    {
        if (_mainWindow?.ApplyHotkey(hk) != true) return false;
        Settings.Hotkey = hk.ToString();
        Settings.Save();
        if (_tray is not null) _tray.ToolTipText = $"openAlfred · {Settings.Hotkey} 唤起";
        return true;
    }

    /// <summary>录制新热键前挂起当前绑定，避免旧组合键拦截按键。</summary>
    public void SuspendHotkey() => _mainWindow?.SuspendHotkey();

    /// <summary>退出录制时按设置恢复绑定。</summary>
    public void ResumeHotkey() => _mainWindow?.ApplyHotkey(Hotkey.Parse(Settings.Hotkey));

    public void ShowSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow();
        _settingsWindow.Show();
    }

    /// <summary>开始菜单 Programs 目录（用户级在前，同名时优先保留）。</summary>
    private static IEnumerable<string> StartMenuRoots()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs");
    }

    /// <summary>开机自启：写 HKCU Run 键。</summary>
    public static void ApplyStartupSetting(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null) return;
            if (enable)
                key.SetValue("openAlfred", $"\"{Environment.ProcessPath}\"");
            else
                key.DeleteValue("openAlfred", throwOnMissingValue: false);
        }
        catch (Exception) { /* 无权限时忽略 */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 次实例未初始化服务也会走这里，全程尽力清理
        try
        {
            _activateWait?.Unregister(null);
            _activateEvent?.Dispose();
            _tray?.Dispose();
            FileIndex?.Dispose();
            _mutex?.ReleaseMutex();
        }
        catch (Exception) { /* 忽略清理失败 */ }
        base.OnExit(e);
    }
}
