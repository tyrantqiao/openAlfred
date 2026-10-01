using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenAlfred.Core.Json;

public enum JsonStatus
{
    Ok,
    /// <summary>内容不完整（如括号未闭合且尚在输入中），静默不显示。</summary>
    Incomplete,
    Invalid,
}

public readonly record struct JsonAnalysis(
    JsonStatus Status,
    string? Formatted,
    string? Minified,
    int NodeCount,
    int CharCount,
    string? Error,
    int ErrorLine,
    int ErrorColumn);

/// <summary>JSON 校验 / 格式化 / 压缩服务。</summary>
public static class JsonService
{
    /// <summary>判断输入是否意图为 JSON（以 { 或 [ 开头）。</summary>
    public static bool LooksLikeJson(string input)
    {
        var trimmed = input.TrimStart();
        return trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '[');
    }

    public static JsonAnalysis Analyze(string input)
    {
        var text = input.Trim();
        if (text.Length == 0)
            return new JsonAnalysis(JsonStatus.Incomplete, null, null, 0, 0, null, 0, 0);

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            // 尾部括号尚未输入完整视为不完整
            var open = text.Count(c => c is '{' or '[') ;
            var close = text.Count(c => c is '}' or ']');
            if (open > close && (ex.BytePositionInLine ?? 0) >= (long)text.Length - 1)
                return new JsonAnalysis(JsonStatus.Incomplete, null, null, 0, 0, null, 0, 0);

            return new JsonAnalysis(
                JsonStatus.Invalid, null, null, 0, text.Length,
                CleanMessage(ex.Message), (int)(ex.LineNumber ?? 1), (int)(ex.BytePositionInLine ?? 0) + 1);
        }

        if (node is null)
            return new JsonAnalysis(JsonStatus.Invalid, null, null, 0, text.Length, "内容为 null", 1, 1);

        var formatted = node.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        var minified = node.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

        return new JsonAnalysis(
            JsonStatus.Ok, formatted, minified, CountNodes(node), text.Length, null, 0, 0);
    }

    private static int CountNodes(JsonNode node)
    {
        var count = 1;
        switch (node)
        {
            case JsonObject obj:
                foreach (var (_, value) in obj)
                    if (value is not null) count += CountNodes(value);
                break;
            case JsonArray arr:
                foreach (var item in arr)
                    if (item is not null) count += CountNodes(item);
                break;
        }
        return count;
    }

    /// <summary>把 STJ 的英文异常信息压缩成一行简短中文可读文本。</summary>
    private static string CleanMessage(string message)
    {
        // 保留全文但去掉换行
        return message.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    /// <summary>生成结果行的摘要文本，如「合法 JSON · 12 个节点 · 340 字符」。</summary>
    public static string Summarize(JsonAnalysis analysis)
    {
        var sb = new StringBuilder("合法 JSON · ");
        sb.Append(analysis.NodeCount).Append(" 个节点 · ").Append(analysis.CharCount).Append(" 字符");
        return sb.ToString();
    }
}
