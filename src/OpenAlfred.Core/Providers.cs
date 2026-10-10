using OpenAlfred.Core.Apps;
using OpenAlfred.Core.Calculation;
using OpenAlfred.Core.Clipboard;
using OpenAlfred.Core.Files;
using OpenAlfred.Core.Json;
using OpenAlfred.Core.Time;

namespace OpenAlfred.Core;

/// <summary>计算器：纯算式自动识别，Enter 复制结果。</summary>
public sealed class CalculatorProvider : IQueryProvider
{
    public string Keyword => "";
    public int Priority => 0; // 算式命中时置顶

    public bool CanHandle(QueryContext context)
        => context.Keyword.Length == 0 && Calculator.LooksLikeExpression(context.Raw);

    public Task<IReadOnlyList<QueryResult>> ExecuteAsync(QueryContext context, CancellationToken cancellationToken = default)
    {
        var result = Calculator.Evaluate(context.Raw);
        IReadOnlyList<QueryResult> list = result.Status switch
        {
            CalcStatus.Ok =>
            [
                new QueryResult
                {
                    Title = Calculator.Format(result.Value),
                    Subtitle = context.Raw,
                    Source = "calc",
                    Action = ResultActionKind.CopyText,
                    Payload = Calculator.Format(result.Value),
                    Score = 1000,
                },
            ],
            CalcStatus.Error =>
            [
                new QueryResult
                {
                    Title = context.Raw,
                    Source = "calc",
                    Error = result.Error,
                    Score = 1000,
                },
            ],
            _ => Array.Empty<QueryResult>(),
        };
        return Task.FromResult(list);
    }
}

/// <summary>JSON 转换：json 前缀或自动识别 { / [ 开头内容。</summary>
public sealed class JsonProvider : IQueryProvider
{
    public string Keyword => "json";
    public int Priority => 0;

    public bool CanHandle(QueryContext context)
        => context.Keyword.Length == 0 && JsonService.LooksLikeJson(context.Raw);

    public Task<IReadOnlyList<QueryResult>> ExecuteAsync(QueryContext context, CancellationToken cancellationToken = default)
    {
        // 关键词命中时参数在 Argument；自动识别时整串即内容
        var content = context.Keyword == "json" ? context.Argument : context.Raw;
        var analysis = JsonService.Analyze(content);

        IReadOnlyList<QueryResult> list = analysis.Status switch
        {
            JsonStatus.Ok =>
            [
                new QueryResult
                {
                    Title = JsonService.Summarize(analysis),
                    Subtitle = FirstLines(analysis.Formatted!, 3),
                    Source = "json",
                    Action = ResultActionKind.CopyText,
                    Payload = analysis.Formatted,
                    SecondaryAction = ResultActionKind.CopyText,
                    SecondaryPayload = analysis.Minified,
                    Score = 1000,
                },
            ],
            JsonStatus.Invalid =>
            [
                new QueryResult
                {
                    Title = "JSON 语法错误",
                    Subtitle = $"第 {analysis.ErrorLine} 行 第 {analysis.ErrorColumn} 列",
                    Source = "json",
                    Error = analysis.Error,
                    Score = 1000,
                },
            ],
            _ => Array.Empty<QueryResult>(),
        };
        return Task.FromResult(list);
    }

    private static string FirstLines(string text, int count)
    {
        var lines = text.Split('\n');
        var head = string.Join("\n", lines.Take(count)).Replace("\r", "");
        return lines.Length > count ? head + "\n…" : head;
    }
}

/// <summary>时间查询：time / date 关键词。</summary>
public sealed class TimeProvider : IQueryProvider
{
    public string Keyword => "time";
    public int Priority => 0;

    // date 作为别名同样路由到这里
    public bool CanHandle(QueryContext context) => false;

    public Task<IReadOnlyList<QueryResult>> ExecuteAsync(QueryContext context, CancellationToken cancellationToken = default)
    {
        var info = TimeService.Resolve(context.Argument);
        if (info.Status != TimeStatus.Ok)
        {
            IReadOnlyList<QueryResult> error =
            [
                new QueryResult
                {
                    Title = "无法解析时间",
                    Source = "time",
                    Error = info.Error,
                    Score = 1000,
                },
            ];
            return Task.FromResult(error);
        }

        var iso = info.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
        var display = info.Now.ToString("yyyy年M月d日") + " " + TimeService.WeekdayName(info.Now.DayOfWeek);
        IReadOnlyList<QueryResult> list =
        [
            new QueryResult
            {
                Title = $"{info.Now:HH:mm:ss} · {info.ZoneLabel}",
                Subtitle = $"{display} · {TimeService.OffsetVsLocal(info)} · {iso}",
                Source = "time",
                Action = ResultActionKind.CopyText,
                Payload = iso,
                Score = 1000,
            },
        ];
        return Task.FromResult(list);
    }
}

/// <summary>剪贴板历史：clip 关键词过滤，无前缀时也参与内容命中。</summary>
public sealed class ClipboardProvider : IQueryProvider
{
    private readonly ClipboardStore _store;

    public ClipboardProvider(ClipboardStore store) => _store = store;

    public string Keyword => "clip";
    public int Priority => 30;

    public bool CanHandle(QueryContext context) => context.Keyword.Length == 0;

    public Task<IReadOnlyList<QueryResult>> ExecuteAsync(QueryContext context, CancellationToken cancellationToken = default)
    {
        var isClipMode = context.Keyword == "clip";
        var query = isClipMode ? context.Argument : context.Raw;
        // 直达剪贴板历史（clip 关键词）展示更多条目；无前缀兜底仍只取前几条
        var entries = _store.Search(query, limit: isClipMode ? 50 : 5);

        List<QueryResult> results = new();
        foreach (var entry in entries)
        {
            var score = string.IsNullOrEmpty(query)
                ? 500 - results.Count
                : FuzzyMatcher.Score(entry.Content, query) + Math.Min(entry.Count, 10) * 0.1;
            var meta = RelativeTime.Format(entry.Time)
                + (entry.Count > 1 ? $" · ×{entry.Count}" : "");

            if (entry.Kind == "image")
            {
                results.Add(new QueryResult
                {
                    Title = entry.Content,
                    Subtitle = "图片 · Enter 粘贴，Ctrl+Enter 拷贝",
                    Meta = meta,
                    Source = "clip",
                    Action = ResultActionKind.PasteClipboard,
                    Payload = entry.Id,
                    SecondaryAction = ResultActionKind.CopyClipboardEntry,
                    ImagePath = entry.ImagePath,
                    HasThumbnail = entry.ImagePath is not null && System.IO.File.Exists(entry.ImagePath),
                    Score = score,
                });
                continue;
            }

            // 文本：首行做标题，多余行拼接为多行预览
            var lines = entry.Content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var title = lines[0];
            if (title.Length > 120) title = title[..120] + "…";
            string? preview = null;
            if (lines.Length > 1)
            {
                var rest = string.Join("\n", lines.Skip(1).Take(3));
                if (rest.Length > 300) rest = rest[..300] + "…";
                if (lines.Length > 4) rest += "\n…";
                preview = rest;
            }

            results.Add(new QueryResult
            {
                Title = string.IsNullOrEmpty(title) ? "（空行）" : title,
                Subtitle = preview is null ? "文本 · Enter 粘贴，Ctrl+Enter 拷贝" : null,
                Preview = preview,
                Meta = meta,
                Source = "clip",
                Action = ResultActionKind.PasteClipboard,
                Payload = entry.Id,
                SecondaryAction = ResultActionKind.CopyClipboardEntry,
                Score = score,
            });
        }
        return Task.FromResult<IReadOnlyList<QueryResult>>(results);
    }
}

/// <summary>应用启动器：app 前缀或无前缀参与，启动开始菜单快捷方式。</summary>
public sealed class AppProvider : IQueryProvider
{
    private readonly AppIndex _index;

    public AppProvider(AppIndex index) => _index = index;

    public string Keyword => "app";
    public int Priority => 10;

    public bool CanHandle(QueryContext context)
        => context.Keyword.Length == 0 && context.Raw.Length >= 2;

    public Task<IReadOnlyList<QueryResult>> ExecuteAsync(QueryContext context, CancellationToken cancellationToken = default)
    {
        var query = context.Keyword == "app" ? context.Argument : context.Raw;
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult<IReadOnlyList<QueryResult>>(Array.Empty<QueryResult>());

        var apps = _index.Search(query, limit: 5);
        List<QueryResult> results = new();
        foreach (var app in apps)
        {
            results.Add(new QueryResult
            {
                Title = app.Name,
                Subtitle = app.Category is null ? "应用" : $"应用 · {app.Category}",
                Source = "app",
                Action = ResultActionKind.OpenPath,
                Payload = app.Path,
                SecondaryAction = ResultActionKind.RevealInExplorer,
                SecondaryPayload = app.Path,
                // 应用命中优先于同名文件
                Score = FuzzyMatcher.Score(app.Name, query) + 300,
            });
        }
        return Task.FromResult<IReadOnlyList<QueryResult>>(results);
    }
}

/// <summary>文件检索：file 前缀或无前缀兜底。</summary>
public sealed class FileProvider : IQueryProvider
{
    private readonly FileIndex _index;

    public FileProvider(FileIndex index) => _index = index;

    public string Keyword => "file";
    public int Priority => 20;

    public bool CanHandle(QueryContext context)
        => context.Keyword.Length == 0 && context.Raw.Length >= 2;

    public Task<IReadOnlyList<QueryResult>> ExecuteAsync(QueryContext context, CancellationToken cancellationToken = default)
    {
        var query = context.Keyword == "file" ? context.Argument : context.Raw;
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult<IReadOnlyList<QueryResult>>(Array.Empty<QueryResult>());

        if (_index.Phase == IndexPhase.Building)
        {
            IReadOnlyList<QueryResult> loading =
            [
                new QueryResult { Title = "文件索引建立中…", Source = "file", Score = 1 },
            ];
            return Task.FromResult(loading);
        }

        var entries = _index.Search(query, limit: 5);
        List<QueryResult> results = new();
        foreach (var entry in entries)
        {
            results.Add(new QueryResult
            {
                Title = entry.IsDirectory ? entry.Name + "（文件夹）" : entry.Name,
                Subtitle = entry.FullPath,
                Meta = RelativeTime.Format(entry.Modified)
                       + (entry.IsDirectory ? "" : " · " + RelativeTime.FormatSize(entry.Length)),
                Source = "file",
                Action = ResultActionKind.OpenPath,
                Payload = entry.FullPath,
                SecondaryAction = ResultActionKind.RevealInExplorer,
                SecondaryPayload = entry.FullPath,
                Score = FuzzyMatcher.Score(entry.Name, query),
            });
        }
        return Task.FromResult<IReadOnlyList<QueryResult>>(results);
    }
}

/// <summary>
/// 查询路由器：解析关键词前缀 → 分派 Provider → 合并排序截断。
/// </summary>
public sealed class QueryRouter
{
    /// <summary>保留关键词（小写）。date 是 time 的别名。</summary>
    private static readonly string[] ReservedKeywords = ["app", "clip", "file", "json", "time", "date"];

    private readonly List<IQueryProvider> _providers;

    public QueryRouter(IEnumerable<IQueryProvider> providers)
        => _providers = providers.OrderBy(p => p.Priority).ToList();

    /// <summary>解析输入为查询上下文。</summary>
    public static QueryContext Parse(string raw)
    {
        var trimmed = raw.TrimStart();
        if (!trimmed.StartsWith('>'))
            return new QueryContext(raw, "", trimmed);

        var command = trimmed[1..].TrimStart();
        var spaceIndex = 0;
        while (spaceIndex < command.Length && !char.IsWhiteSpace(command[spaceIndex]))
            spaceIndex++;
        var head = command[..spaceIndex].ToLowerInvariant();

        if (ReservedKeywords.Contains(head))
            return new QueryContext(raw, head, command[spaceIndex..].TrimStart());

        return new QueryContext(raw, "", trimmed);
    }

    /// <summary>全部关键词，供 Tab 补全。</summary>
    public static IReadOnlyList<string> Keywords => ReservedKeywords;

    public async Task<IReadOnlyList<QueryResult>> RouteAsync(string raw, CancellationToken cancellationToken = default)
    {
        var context = Parse(raw);
        if (context.Raw.Trim().Length == 0 || (context.Raw.TrimStart().StartsWith('>') && context.Keyword.Length == 0))
            return Array.Empty<QueryResult>();

        var participants = new List<(IQueryProvider Provider, int Priority)>();
        if (context.Keyword.Length > 0)
        {
            // "date" 别名同样交给 time Provider
            var keyword = context.Keyword == "date" ? "time" : context.Keyword;
            foreach (var p in _providers.Where(p => p.Keyword == keyword))
                participants.Add((p, p.Priority));
        }
        else
        {
            foreach (var p in _providers.Where(p => p.CanHandle(context)))
                participants.Add((p, p.Priority));

            // 无前缀时应用、文件与剪贴板兜底参与混排
            foreach (var p in _providers.Where(p =>
                         p.Keyword is "app" or "file" or "clip"
                         && !participants.Any(x => x.Provider == p)
                         && p.CanHandle(context)))
                participants.Add((p, p.Priority));
        }

        var all = new List<QueryResult>();
        foreach (var (provider, priority) in participants)
        {
            try
            {
                var results = await provider.ExecuteAsync(context, cancellationToken);
                // 同分时按 Provider 优先级稳定排序
                all.AddRange(results.Select(r => r with { Score = r.Score - priority * 0.001 }));
            }
            catch (Exception)
            {
                // 单个 Provider 异常不影响其他结果
            }
        }

        return all
            .OrderByDescending(r => r.Score)
            .Take(context.Keyword == "clip" ? 50 : 8)
            .ToList();
    }
}
