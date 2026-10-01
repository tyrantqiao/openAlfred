namespace OpenAlfred.Core.Apps;

public sealed record AppEntry(string Name, string Path, string? Category);

/// <summary>
/// 应用启动器索引：扫描开始菜单快捷方式（.lnk / .url），
/// 名称取文件名（去扩展名），分类取 Programs 下的子文件夹名。
/// 启动时直接 ShellExecute 快捷方式文件，由 Windows 解析目标，无需 COM 解链。
/// </summary>
public sealed class AppIndex
{
    /// <summary>这些目录下的快捷方式是噪音（开机启动项副本等）。</summary>
    private static readonly HashSet<string> SkipFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "StartUp",
    };

    private readonly List<AppEntry> _entries = new();
    private readonly object _gate = new();

    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    /// <summary>同步构建（开始菜单通常只有几百个文件，毫秒级完成）。</summary>
    public void Build(IEnumerable<string> startMenuRoots)
    {
        var found = new List<AppEntry>();
        foreach (var root in startMenuRoots)
        {
            if (!Directory.Exists(root)) continue;
            ScanRoot(root, found);
        }

        // 同名应用去重（用户级优先于全体用户级，先扫的先保留）
        var deduped = found
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        lock (_gate)
        {
            _entries.Clear();
            _entries.AddRange(deduped);
        }
    }

    private static void ScanRoot(string root, List<AppEntry> found)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories);
        }
        catch (Exception) { return; }

        foreach (var file in files)
        {
            var ext = Path.GetExtension(file);
            if (!ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                && !ext.Equals(".url", StringComparison.OrdinalIgnoreCase))
                continue;

            var relativeDir = Path.GetDirectoryName(file)?[root.Length..];
            if (relativeDir is not null && SkipFolders.Any(s =>
                    relativeDir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Contains(s, StringComparer.OrdinalIgnoreCase)))
                continue;

            var name = Path.GetFileNameWithoutExtension(file);
            var category = CategoryOf(relativeDir);
            found.Add(new AppEntry(name, file, category));
        }
    }

    /// <summary>取 Programs 下第一层子文件夹作为分类名。</summary>
    private static string? CategoryOf(string? relativeDir)
    {
        if (string.IsNullOrEmpty(relativeDir)) return null;
        var parts = relativeDir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);
        // 相对路径首段常为 "Programs"，分类取其后一段
        var meaningful = parts.Where(p => !p.Equals("Programs", StringComparison.OrdinalIgnoreCase)).ToArray();
        return meaningful.Length > 0 ? meaningful[0] : null;
    }

    /// <summary>模糊搜索应用名，命中越完整分越高。</summary>
    public IReadOnlyList<AppEntry> Search(string query, int limit = 5)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        lock (_gate)
        {
            return _entries
                .Select(e => (Entry: e, Score: FuzzyMatcher.Score(e.Name, query)))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Take(limit)
                .Select(x => x.Entry)
                .ToList();
        }
    }
}
