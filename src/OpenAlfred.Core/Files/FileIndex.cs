using System.Collections.Concurrent;

namespace OpenAlfred.Core.Files;

public sealed record FileEntry(string Name, string FullPath, bool IsDirectory, DateTimeOffset Modified, long Length);

public enum IndexPhase
{
    Idle,
    Building,
    Ready,
}

/// <summary>
/// 文件名内存索引：后台扫描指定根目录，FileSystemWatcher 增量维护。
/// 扫描在 ThreadPool 上迭代进行，不阻塞 UI；查询始终读取当前已建好的快照。
/// </summary>
public sealed class FileIndex : IDisposable
{
    private const int MaxDepth = 8;
    private const int MaxEntries = 300_000;

    private readonly ConcurrentDictionary<string, FileEntry> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly CancellationTokenSource _cts = new();
    private volatile int _scanned;
    private volatile int _estimated;

    public IndexPhase Phase { get; private set; } = IndexPhase.Idle;
    /// <summary>索引构建进度事件（0~1，-1 表示完成）。</summary>
    public event EventHandler<int>? ProgressChanged;

    public int Count => _byPath.Count;

    /// <summary>跳过这些目录名（系统/工具目录，噪音大）。</summary>
    private static readonly HashSet<string> SkipDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "$RECYCLE.BIN", "System Volume Information",
        "AppData", ".vscode", "bin", "obj", "__pycache__", ".gradle", ".m2",
        "target", "dist", "build", ".nuget", "Packages",
    };

    public void StartBuild(IEnumerable<string> rootPaths)
    {
        if (Phase == IndexPhase.Building) return;
        Phase = IndexPhase.Building;
        var roots = rootPaths.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // 粗略估算文件总数用于进度显示（限时 2 秒，估不出就不显示百分比）
        _estimated = Estimate(roots);
        _scanned = 0;

        Task.Run(() =>
        {
            foreach (var root in roots)
                ScanDirectory(root, 0);
            foreach (var root in roots)
                Watch(root);
            Phase = IndexPhase.Ready;
            ProgressChanged?.Invoke(this, -1);
        }, _cts.Token);
    }

    private int Estimate(List<string> roots)
    {
        try
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            return roots.Sum(root =>
            {
                try
                {
                    return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                        .Take(50_000).Count(_ => !cts.IsCancellationRequested);
                }
                catch (Exception) { return 0; }
            });
        }
        catch (Exception) { return 0; }
    }

    private void ScanDirectory(string dir, int depth)
    {
        if (depth > MaxDepth || _byPath.Count >= MaxEntries || _cts.IsCancellationRequested) return;

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(dir);
        }
        catch (Exception)
        {
            return; // 无权限/坏链接直接跳过
        }

        foreach (var entry in entries)
        {
            if (_cts.IsCancellationRequested || _byPath.Count >= MaxEntries) return;
            var name = Path.GetFileName(entry);
            if (SkipDirectories.Contains(name)) continue;

            try
            {
                var isDir = Directory.Exists(entry);
                var info = isDir
                    ? new DirectoryInfo(entry)
                    : (FileSystemInfo)new FileInfo(entry);
                _byPath.TryAdd(entry, new FileEntry(
                    name, entry, isDir, info.LastWriteTime, isDir ? 0 : ((FileInfo)info).Length));
                _scanned++;
                if (_scanned % 2000 == 0)
                    ReportProgress();

                if (isDir) ScanDirectory(entry, depth + 1);
            }
            catch (Exception) { continue; }
        }
    }

    private void ReportProgress()
    {
        if (_estimated <= 0) return;
        var percent = Math.Min(99, _scanned * 100 / _estimated);
        ProgressChanged?.Invoke(this, percent);
    }

    private void Watch(string root)
    {
        try
        {
            var watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                InternalBufferSize = 64 * 1024,
            };
            watcher.Created += (sender, e) =>
            {
                if (e.FullPath is null || SkipPath(e.FullPath)) return;
                Upsert(e.FullPath);
            };
            watcher.Deleted += (sender, e) =>
            {
                if (e.FullPath is not null) _byPath.TryRemove(e.FullPath, out FileEntry? _);
            };
            watcher.Renamed += (sender, e) =>
            {
                if (e.OldFullPath is not null) _byPath.TryRemove(e.OldFullPath, out FileEntry? _);
                if (e.FullPath is not null && !SkipPath(e.FullPath)) Upsert(e.FullPath);
            };
            watcher.Error += (sender, e) => { /* 缓冲区溢出时不做全量重建，容忍少量漂移 */ };
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
        catch (Exception) { /* 目录不存在等 */ }
    }

    private static bool SkipPath(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => SkipDirectories.Contains(p));
    }

    private void Upsert(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                _byPath[path] = new FileEntry(info.Name, path, false, info.LastWriteTime, info.Length);
            }
            else if (Directory.Exists(path))
            {
                var info = new DirectoryInfo(path);
                _byPath[path] = new FileEntry(info.Name, path, true, info.LastWriteTime, 0);
            }
        }
        catch (Exception) { }
    }

    /// <summary>模糊搜索文件名，按分数降序；分数相同新文件优先。</summary>
    public IReadOnlyList<FileEntry> Search(string query, int limit = 5)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var results = new List<(FileEntry Entry, double Score)>(limit * 4);

        foreach (var entry in _byPath.Values)
        {
            var score = FuzzyMatcher.Score(entry.Name, query);
            if (score <= 0) continue;
            results.Add((entry, score));
        }

        return results
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Entry.Modified)
            .Take(limit)
            .Select(x => x.Entry)
            .ToList();
    }

    public void Dispose()
    {
        _cts.Cancel();
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();
    }
}
