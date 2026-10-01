using Xunit;

namespace OpenAlfred.Core.Tests;

public class FuzzyMatcherTests
{
    [Theory]
    [InlineData("resume2026.tex", "res", 100d)]      // 前缀最高分
    [InlineData("my_resume_v2.md", "res", 0d, true)] // 词首命中
    [InlineData("readme.md", "xyz", 0d)]             // 不命中
    public void Scores_Match_Tiers(string text, string query, double _, bool isWordStart = false)
    {
        var score = FuzzyMatcher.Score(text, query);
        if (isWordStart)
            Assert.True(score > 0, $"{text} 应命中 {query}");
        else if (_ == 0)
            Assert.Equal(0, score);
        else
            Assert.True(score >= 90, $"前缀命中应接近满分，实际 {score}");
    }

    [Fact]
    public void Prefix_Beats_Contains_Beats_Subsequence()
    {
        var prefix = FuzzyMatcher.Score("resume2026.tex", "res");
        var contains = FuzzyMatcher.Score("mygreatresume.tex", "res");
        var subsequence = FuzzyMatcher.Score("r-x-c-e.md", "rce");

        Assert.True(prefix > contains);
        Assert.True(contains > subsequence);
        Assert.True(subsequence > 0);
    }

    [Fact]
    public void Empty_Query_Or_Text_Scores_Zero()
    {
        Assert.Equal(0, FuzzyMatcher.Score("file", ""));
        Assert.Equal(0, FuzzyMatcher.Score("", "q"));
    }

    [Fact]
    public void Matching_Is_Case_Insensitive()
    {
        Assert.Equal(
            FuzzyMatcher.Score("Resume.tex", "res"),
            FuzzyMatcher.Score("resume.tex", "RES"));
    }
}
