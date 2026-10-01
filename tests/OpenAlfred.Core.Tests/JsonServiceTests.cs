using OpenAlfred.Core.Json;
using Xunit;

namespace OpenAlfred.Core.Tests;

public class JsonServiceTests
{
    [Fact]
    public void Formats_Valid_Json()
    {
        var result = JsonService.Analyze("""{"name":"openAlfred","ver":1,"beta":true}""");
        Assert.Equal(JsonStatus.Ok, result.Status);
        Assert.NotNull(result.Formatted);
        Assert.Contains("\n", result.Formatted);
        Assert.Equal("""{"name":"openAlfred","ver":1,"beta":true}""", result.Minified);
        Assert.Equal(4, result.NodeCount); // 根对象 + 3 个子节点
    }

    [Fact]
    public void Minify_Strips_Whitespace()
    {
        var result = JsonService.Analyze("{\n  \"a\": [1, 2]\n}");
        Assert.Equal(JsonStatus.Ok, result.Status);
        Assert.Equal("""{"a":[1,2]}""", result.Minified);
    }

    [Fact]
    public void Reports_Error_Line_And_Column()
    {
        var result = JsonService.Analyze("{\n  \"a\": ,\n}");
        Assert.Equal(JsonStatus.Invalid, result.Status);
        Assert.NotNull(result.Error);
        Assert.True(result.ErrorLine >= 1);
    }

    [Fact]
    public void Unclosed_Brace_Is_Incomplete()
    {
        var result = JsonService.Analyze("""{"a": 1""");
        Assert.Equal(JsonStatus.Incomplete, result.Status);
    }

    [Theory]
    [InlineData("{\"a\":1}", true)]
    [InlineData("[1,2]", true)]
    [InlineData("hello", false)]
    [InlineData("  {\"a\":1}", true)]
    public void Detects_Json_Looking_Input(string input, bool expected)
    {
        Assert.Equal(expected, JsonService.LooksLikeJson(input));
    }

    [Fact]
    public void Keeps_Chinese_Unescaped_In_Formatted_Output()
    {
        var result = JsonService.Analyze("""{"msg":"你好"}""");
        Assert.Contains("你好", result.Formatted);
    }
}
