namespace OpenAlfred.Core;

/// <summary>
/// 文件名/文本模糊匹配打分。
/// 规则优先级：完整前缀 &gt; 词首（. _ - 空格 之后）&gt; 包含 &gt; 子序列。
/// 分数区间约 0~100，0 表示不匹配。
/// </summary>
public static class FuzzyMatcher
{
    private static readonly char[] WordSeparators = [' ', '.', '_', '-', '/', '\\', '('];

    public static double Score(string text, string query)
    {
        if (string.IsNullOrEmpty(query)) return 0;
        if (string.IsNullOrEmpty(text)) return 0;

        text = text.ToLowerInvariant();
        query = query.ToLowerInvariant();

        // 1. 前缀命中
        if (text.StartsWith(query, StringComparison.Ordinal))
            return 100 - Math.Min(text.Length - query.Length, 20) * 0.5;

        // 2. 词首命中（每个查询字符都尽量落在词首）
        if (text.Contains(query, StringComparison.Ordinal))
        {
            var idx = text.IndexOf(query, StringComparison.Ordinal);
            var atWordStart = idx > 0 && IsSeparator(text[idx - 1]);
            var baseScore = atWordStart ? 80 : 60;
            // 越靠前分越高
            return baseScore - Math.Min(idx, 20) * 0.5 - Math.Min(text.Length - query.Length, 30) * 0.2;
        }

        // 3. 子序列命中
        return SubsequenceScore(text, query);
    }

    private static double SubsequenceScore(string text, string query)
    {
        double score = 0;
        int ti = 0, consecutive = 0;
        foreach (var qc in query)
        {
            var found = -1;
            while (ti < text.Length)
            {
                if (text[ti] == qc) { found = ti; break; }
                ti++;
            }
            if (found < 0) return 0; // 有字符不命中
            consecutive++;
            score += consecutive >= 2 ? 4 : 2;
            ti++;
        }
        // 子序列总分压低，保证低于任何包含命中
        return Math.Min(score, 38);
    }

    private static bool IsSeparator(char c) => Array.IndexOf(WordSeparators, c) >= 0;
}
