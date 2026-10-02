using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenAlfred.App.Services;

public enum ThemeMode
{
    /// <summary>跟随系统。</summary>
    System,
    Light,
    Dark,
}

public sealed class AppSettings
{
    [JsonPropertyName("hideOnLostFocus")]
    public bool HideOnLostFocus { get; set; } = true;

    [JsonPropertyName("theme")]
    public ThemeMode Theme { get; set; } = ThemeMode.System;

    [JsonPropertyName("runAtStartup")]
    public bool RunAtStartup { get; set; }

    [JsonPropertyName("clipboardPaused")]
    public bool ClipboardPaused { get; set; }

    /// <summary>全局唤起热键，如 "Alt+Space"；默认 Alt+Space。</summary>
    [JsonPropertyName("hotkey")]
    public string Hotkey { get; set; } = Services.Hotkey.DefaultString;

    /// <summary>一键打开剪贴板历史的全局热键；默认 Ctrl+Alt+V。</summary>
    [JsonPropertyName("clipboardHotkey")]
    public string ClipboardHotkey { get; set; } = Services.Hotkey.ClipboardDefaultString;

    /// <summary>最小化时收进系统托盘（常驻后台）；关闭则用系统最小化。</summary>
    [JsonPropertyName("minimizeToTray")]
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>启动后直接最小化到托盘，不弹出主窗口。</summary>
    [JsonPropertyName("startMinimized")]
    public bool StartMinimized { get; set; }

    /// <summary>最小化到托盘时显示一次气泡提示。</summary>
    [JsonPropertyName("trayTipEnabled")]
    public bool TrayTipEnabled { get; set; } = true;

    /// <summary>文件索引根目录；空表示使用默认（用户目录/桌面/文档/下载）。</summary>
    [JsonPropertyName("indexRoots")]
    public List<string> IndexRoots { get; set; } = new();

    [JsonIgnore]
    public string DataDir { get; set; }

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public AppSettings(string? dataDir = null)
    {
        DataDir = dataDir
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "openAlfred");
    }

    private string FilePath => Path.Combine(DataDir, "settings.json");

    public static AppSettings Load()
    {
        var settings = new AppSettings();
        try
        {
            if (File.Exists(settings.FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settings.FilePath));
                if (loaded is not null)
                {
                    loaded.DataDir = settings.DataDir;
                    return loaded;
                }
            }
        }
        catch (Exception) { /* 损坏时用默认值 */ }
        return settings;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, WriteOptions));
        }
        catch (IOException) { }
    }

    /// <summary>默认索引根目录。</summary>
    public IReadOnlyList<string> ResolveIndexRoots()
    {
        if (IndexRoots.Count > 0) return IndexRoots;
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        List<string> candidates =
        [
            profile,
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(profile, "Downloads"),
        ];
        return candidates.Where(Directory.Exists).Distinct().ToList();
    }
}
