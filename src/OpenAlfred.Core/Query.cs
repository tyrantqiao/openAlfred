namespace OpenAlfred.Core;

/// <summary>路由后的查询上下文。</summary>
/// <param name="Raw">用户原始输入。</param>
/// <param name="Keyword">命中的功能关键词；空串表示无前缀输入。</param>
/// <param name="Argument">关键词之后的参数部分（已去前导空格）。</param>
public readonly record struct QueryContext(string Raw, string Keyword, string Argument);

/// <summary>结果项被 Enter / Ctrl+Enter 触发时，由 App 层执行的动作类型。</summary>
public enum ResultActionKind
{
    None,
    /// <summary>把 Payload 写入系统剪贴板。</summary>
    CopyText,
    /// <summary>以默认程序打开 Payload 指定的路径。</summary>
    OpenPath,
    /// <summary>在资源管理器中定位 Payload 指定的路径。</summary>
    RevealInExplorer,
    /// <summary>把剪贴板记录（Payload = 记录 Id）写回并模拟粘贴。</summary>
    PasteClipboard,
    /// <summary>把剪贴板记录（Payload = 记录 Id）的文本/图片内容拷贝到剪切板（不粘贴、不关窗）。</summary>
    CopyClipboardEntry,
}

/// <summary>一条可展示的查询结果。</summary>
public sealed record QueryResult
{
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    /// <summary>右侧辅助信息（时间、大小等）。</summary>
    public string? Meta { get; init; }
    /// <summary>来源模块标识：calc / clip / file / json / time。</summary>
    public required string Source { get; init; }
    public ResultActionKind Action { get; init; } = ResultActionKind.None;
    /// <summary>动作载荷（要复制的文本或要打开的路径，或剪贴板记录 Id）。</summary>
    public string? Payload { get; init; }
    public ResultActionKind SecondaryAction { get; init; } = ResultActionKind.None;
    public string? SecondaryPayload { get; init; }
    /// <summary>错误提示行（红色小字），null 表示正常。</summary>
    public string? Error { get; init; }
    /// <summary>多行预览正文（剪贴板历史等富展示行使用，允许换行）。</summary>
    public string? Preview { get; init; }
    /// <summary>缩略图绝对路径（剪贴板图片历史）；仅表现层使用，不影响动作。</summary>
    public string? ImagePath { get; init; }
    /// <summary>是否用缩略图代替左侧图标展示（图片历史）。</summary>
    public bool HasThumbnail { get; init; }
    /// <summary>排序分，越大越靠前。</summary>
    public double Score { get; init; }
}

/// <summary>功能模块统一接口：一个 Provider 对应一个关键词/自动识别能力。</summary>
public interface IQueryProvider
{
    /// <summary>触发关键词（小写）；空串表示靠 CanHandle 自动识别。</summary>
    string Keyword { get; }
    /// <summary>关键词命中时的固定优先级（小者靠前）。</summary>
    int Priority { get; }
    /// <summary>无前缀输入时是否参与。</summary>
    bool CanHandle(QueryContext context);
    Task<IReadOnlyList<QueryResult>> ExecuteAsync(QueryContext context, CancellationToken cancellationToken = default);
}
