using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenAlfred.Core.Clipboard;

public sealed class ClipboardEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>text 或 image。</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "text";
    /// <summary>文本内容；图片时为显示摘要。</summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
    /// <summary>图片 PNG 文件绝对路径（Kind=image 时有效）。</summary>
    [JsonPropertyName("imagePath")]
    public string? ImagePath { get; set; }
    [JsonPropertyName("time")]
    public DateTimeOffset Time { get; set; } = DateTimeOffset.Now;
}

/// <summary>
/// 剪贴板历史存储：内存列表 + JSON 文件持久化，FIFO 上限 200 条。
/// 线程安全：监听线程写入、UI 线程读取。
/// </summary>
public sealed class ClipboardStore : IDisposable
{
    public const int MaxEntries = 200;

    private readonly object _gate = new();
    private readonly List<ClipboardEntry> _entries = new();
    private readonly string _filePath;

    public ClipboardStore(string? filePath = null)
    {
        _filePath = filePath
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "openAlfred", "clipboard.json");
        Load();
    }

    public event EventHandler? HistoryChanged;

    public IReadOnlyList<ClipboardEntry> Snapshot()
    {
        lock (_gate) return _entries.ToList();
    }

    /// <summary>追加文本记录；与最近一条内容相同时忽略。</summary>
    public void AddText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (_gate)
        {
            if (_entries.Count > 0
                && _entries[0].Kind == "text"
                && string.Equals(_entries[0].Content, text, StringComparison.Ordinal))
                return;
            _entries.Insert(0, new ClipboardEntry { Kind = "text", Content = text });
            Trim();
        }
        Save();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>追加图片记录（PNG 已写入 imagePath）。</summary>
    public void AddImage(string imagePath)
    {
        lock (_gate)
        {
            _entries.Insert(0, new ClipboardEntry
            {
                Kind = "image",
                Content = Path.GetFileName(imagePath),
                ImagePath = imagePath,
            });
            Trim();
        }
        Save();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public ClipboardEntry? Find(string id)
    {
        lock (_gate) return _entries.FirstOrDefault(e => e.Id == id);
    }

    public void Remove(string id)
    {
        bool removed;
        lock (_gate)
        {
            var entry = _entries.FirstOrDefault(e => e.Id == id);
            removed = entry is not null;
            if (entry is not null)
            {
                _entries.Remove(entry);
                if (entry.Kind == "image" && entry.ImagePath is not null && File.Exists(entry.ImagePath))
                    try { File.Delete(entry.ImagePath); } catch (IOException) { /* 尽力而为 */ }
            }
        }
        if (removed)
        {
            Save();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            foreach (var entry in _entries.Where(e => e.Kind == "image" && e.ImagePath is not null))
                try { if (File.Exists(entry.ImagePath)) File.Delete(entry.ImagePath); } catch (IOException) { }
            _entries.Clear();
        }
        Save();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>按关键词过滤（模糊打分），空查询返回全部（截断到 limit）。</summary>
    public IReadOnlyList<ClipboardEntry> Search(string query, int limit = 5)
    {
        var snapshot = Snapshot();
        if (string.IsNullOrWhiteSpace(query))
            return snapshot.Take(limit).ToList();

        return snapshot
            .Select(e => (Entry: e, Score: FuzzyMatcher.Score(e.Content, query)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Entry.Time)
            .Take(limit)
            .Select(x => x.Entry)
            .ToList();
    }

    private void Trim()
    {
        while (_entries.Count > MaxEntries)
        {
            var last = _entries[^1];
            _entries.RemoveAt(_entries.Count - 1);
            if (last.Kind == "image" && last.ImagePath is not null && File.Exists(last.ImagePath))
                try { File.Delete(last.ImagePath); } catch (IOException) { }
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var loaded = JsonSerializer.Deserialize<List<ClipboardEntry>>(File.ReadAllText(_filePath));
            if (loaded is not null)
                _entries.AddRange(loaded.Where(e =>
                    e.Kind == "text" || (e.ImagePath is not null && File.Exists(e.ImagePath))));
        }
        catch (JsonException) { /* 损坏则从空历史开始 */ }
        catch (IOException) { }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            string json;
            lock (_gate)
            {
                json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = false });
            }
            File.WriteAllText(_filePath, json);
        }
        catch (IOException) { /* 持久化失败不影响内存历史 */ }
    }

    public void Dispose() { }
}
