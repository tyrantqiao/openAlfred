using OpenAlfred.Core.Clipboard;
using OpenAlfred.Core.Files;
using Xunit;

namespace OpenAlfred.Core.Tests;

public class QueryRouterTests
{
    private static QueryRouter BuildRouter(ClipboardStore store, FileIndex index) =>
        new(new IQueryProvider[]
        {
            new CalculatorProvider(),
            new JsonProvider(),
            new TimeProvider(),
            new ClipboardProvider(store),
            new FileProvider(index),
        });

    [Fact]
    public async Task Expression_Routes_To_Calculator_First()
    {
        using var store = new ClipboardStore(NewTempPath());
        using var index = new FileIndex();
        var router = BuildRouter(store, index);

        var results = await router.RouteAsync("1+2*3");
        Assert.Equal("calc", results[0].Source);
        Assert.Equal("7", results[0].Title);
    }

    [Fact]
    public async Task Keyword_Prefix_Routes_To_Module()
    {
        using var store = new ClipboardStore(NewTempPath());
        using var index = new FileIndex();
        var router = BuildRouter(store, index);

        var results = await router.RouteAsync("time UTC+9");
        Assert.All(results, r => Assert.Equal("time", r.Source));
    }

    [Fact]
    public async Task Date_Alias_Routes_To_Time()
    {
        using var store = new ClipboardStore(NewTempPath());
        using var index = new FileIndex();
        var router = BuildRouter(store, index);

        var results = await router.RouteAsync("date");
        Assert.Equal("time", results[0].Source);
    }

    [Fact]
    public async Task Json_Prefix_Routes_To_Json()
    {
        using var store = new ClipboardStore(NewTempPath());
        using var index = new FileIndex();
        var router = BuildRouter(store, index);

        var results = await router.RouteAsync("""json {"a":1}""");
        Assert.Equal("json", results[0].Source);
        Assert.Contains("合法", results[0].Title);
    }

    [Fact]
    public async Task Bare_Input_Falls_Back_To_Clipboard_Search()
    {
        var path = NewTempPath();
        using var store = new ClipboardStore(path);
        store.AddText("hello openAlfred world");
        using var index = new FileIndex();
        var router = BuildRouter(store, index);

        var results = await router.RouteAsync("openAlfred");
        Assert.Contains(results, r => r.Source == "clip");
    }

    [Fact]
    public async Task Empty_Input_Returns_Nothing()
    {
        using var store = new ClipboardStore(NewTempPath());
        using var index = new FileIndex();
        var router = BuildRouter(store, index);

        Assert.Empty(await router.RouteAsync("   "));
    }

    [Fact]
    public void Parse_Extracts_Keyword_And_Argument()
    {
        var context = QueryRouter.Parse("file report");
        Assert.Equal("file", context.Keyword);
        Assert.Equal("report", context.Argument);

        var bare = QueryRouter.Parse("hello world");
        Assert.Equal("", bare.Keyword);

        var keywordOnly = QueryRouter.Parse("clip");
        Assert.Equal("clip", keywordOnly.Keyword);
        Assert.Equal("", keywordOnly.Argument);
    }

    private static string NewTempPath() =>
        Path.Combine(Path.GetTempPath(), $"oa-test-{Guid.NewGuid():N}.json");
}

public class ClipboardStoreTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"oa-clip-{Guid.NewGuid():N}.json");

    [Fact]
    public void Add_And_Search_Text()
    {
        using var store = new ClipboardStore(_tempFile);
        store.AddText("第一段内容");
        store.AddText("第二段 important");

        var all = store.Search("");
        Assert.Equal(2, all.Count);
        // 最新的在前
        Assert.Contains("important", all[0].Content);

        var hit = store.Search("important");
        Assert.Single(hit);
    }

    [Fact]
    public void Duplicate_Consecutive_Text_Ignored()
    {
        using var store = new ClipboardStore(_tempFile);
        store.AddText("same");
        store.AddText("same");
        Assert.Single(store.Snapshot());
    }

    [Fact]
    public void History_Persists_Across_Instances()
    {
        using (var store = new ClipboardStore(_tempFile))
            store.AddText("persisted");

        using var reopened = new ClipboardStore(_tempFile);
        Assert.Contains(reopened.Snapshot(), e => e.Content == "persisted");
    }

    [Fact]
    public void Fifo_Cap_At_MaxEntries()
    {
        using var store = new ClipboardStore(_tempFile);
        for (var i = 0; i < ClipboardStore.MaxEntries + 20; i++)
            store.AddText($"item-{i}");
        Assert.Equal(ClipboardStore.MaxEntries, store.Snapshot().Count);
        // 最新的保留
        Assert.Contains("item-219", store.Snapshot()[0].Content);
    }

    [Fact]
    public void Remove_Deletes_Entry()
    {
        using var store = new ClipboardStore(_tempFile);
        store.AddText("keep");
        store.AddText("drop");
        var drop = store.Snapshot().First(e => e.Content == "drop");
        store.Remove(drop.Id);
        Assert.Single(store.Snapshot());
    }

    public void Dispose()
    {
        try { File.Delete(_tempFile); } catch (IOException) { }
    }
}
