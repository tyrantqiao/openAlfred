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

        var results = await router.RouteAsync(">time UTC+9");
        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Equal("time", r.Source));
    }

    [Fact]
    public async Task Date_Alias_Routes_To_Time()
    {
        using var store = new ClipboardStore(NewTempPath());
        using var index = new FileIndex();
        var router = BuildRouter(store, index);

        var results = await router.RouteAsync(">date");
        Assert.Equal("time", results[0].Source);
    }

    [Fact]
    public async Task Json_Prefix_Routes_To_Json()
    {
        using var store = new ClipboardStore(NewTempPath());
        using var index = new FileIndex();
        var router = BuildRouter(store, index);

        var results = await router.RouteAsync(""">json {"a":1}""");
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
    public async Task Clip_Mode_Exposes_Multiline_Preview_And_Copy_Secondary()
    {
        using var store = new ClipboardStore(NewTempPath());
        store.AddText("第一行\n第二行\n第三行");
        using var index = new FileIndex();
        var router = BuildRouter(store, index);

        var clip = Assert.Single(await router.RouteAsync(">clip"), r => r.Source == "clip");
        Assert.Equal("第一行", clip.Title);
        Assert.NotNull(clip.Preview);
        Assert.Contains("第二行", clip.Preview);
        Assert.Equal(ResultActionKind.PasteClipboard, clip.Action);
        Assert.Equal(ResultActionKind.CopyClipboardEntry, clip.SecondaryAction);
    }

    [Fact]
    public async Task Clip_Image_Entry_Carries_Thumbnail()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"oa-img-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var img = Path.Combine(dir, "pic.png");
            File.WriteAllBytes(img, [9, 8, 7]);
            using var store = new ClipboardStore(NewTempPath());
            store.AddImage(img, "H1");
            using var index = new FileIndex();
            var router = BuildRouter(store, index);

            var clip = Assert.Single(await router.RouteAsync(">clip"), r => r.Source == "clip");
            Assert.True(clip.HasThumbnail);
            Assert.Equal(img, clip.ImagePath);
            Assert.Equal("pic.png", clip.Title);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Parse_Extracts_Keyword_And_Argument()
    {
        var context = QueryRouter.Parse(">file report");
        Assert.Equal("file", context.Keyword);
        Assert.Equal("report", context.Argument);

        var bare = QueryRouter.Parse("hello world");
        Assert.Equal("", bare.Keyword);

        var keywordOnly = QueryRouter.Parse(">clip");
        Assert.Equal("clip", keywordOnly.Keyword);
        Assert.Equal("", keywordOnly.Argument);
    }

    [Theory]
    [InlineData(">clip", "clip", "")]
    [InlineData("> clip", "clip", "")]
    [InlineData("  > CLIP   hello world", "clip", "hello world")]
    [InlineData(">app code", "app", "code")]
    [InlineData("> file report", "file", "report")]
    [InlineData(">json {}", "json", "{}")]
    [InlineData("> time UTC+9", "time", "UTC+9")]
    [InlineData(">date", "date", "")]
    [InlineData(">	clip	hello", "clip", "hello")]
    public void Parse_Command_Prefix(string input, string keyword, string argument)
    {
        var context = QueryRouter.Parse(input);
        Assert.Equal(input, context.Raw);
        Assert.Equal(keyword, context.Keyword);
        Assert.Equal(argument, context.Argument);
    }

    [Theory]
    [InlineData("app")]
    [InlineData("clip")]
    [InlineData("file")]
    [InlineData("json")]
    [InlineData("time")]
    [InlineData("date")]
    public async Task Bare_Keyword_Searches_Files(string keyword)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"oa-route-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, keyword + ".txt");
            File.WriteAllText(file, "");
            using var store = new ClipboardStore(NewTempPath());
            using var index = new FileIndex();
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            index.ProgressChanged += (_, progress) =>
            {
                if (progress == -1) ready.TrySetResult();
            };
            index.StartBuild([dir]);
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var context = QueryRouter.Parse(keyword);
            Assert.Equal("", context.Keyword);
            Assert.Equal(keyword, context.Argument);
            var results = await BuildRouter(store, index).RouteAsync(keyword);
            Assert.Equal(file, Assert.Single(results).Payload);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(">")]
    [InlineData("> ")]
    [InlineData(">unknown")]
    [InlineData(">clipboard")]
    [InlineData("> clipnotes")]
    public async Task Incomplete_Or_Unknown_Command_Does_Not_Search(string input)
    {
        using var store = new ClipboardStore(NewTempPath());
        store.AddText(input);
        using var index = new FileIndex();
        Assert.Empty(await BuildRouter(store, index).RouteAsync(input));
    }

    [Theory]
    [InlineData(">clip")]
    [InlineData("> clip")]
    [InlineData("> CLIP")]
    public async Task Clip_Command_Returns_History_Without_Search_Results(string input)
    {
        using var store = new ClipboardStore(NewTempPath());
        for (var i = 0; i < 60; i++) store.AddText($"item-{i}");
        using var index = new FileIndex();
        var results = await BuildRouter(store, index).RouteAsync(input);
        Assert.Equal(50, results.Count);
        Assert.All(results, result => Assert.Equal("clip", result.Source));
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
    public void Duplicate_Consecutive_Text_Counted_And_Merged()
    {
        using var store = new ClipboardStore(_tempFile);
        store.AddText("same");
        store.AddText("same");
        var snap = store.Snapshot();
        // 不再忽略重复，而是合并为一条并计数
        Assert.Single(snap);
        Assert.Equal(2, snap[0].Count);
    }

    [Fact]
    public void Duplicate_Text_Moves_To_Front_With_Count()
    {
        using var store = new ClipboardStore(_tempFile);
        store.AddText("a");
        store.AddText("b");
        store.AddText("a");
        var snap = store.Snapshot();
        Assert.Equal(2, snap.Count);
        Assert.Equal("a", snap[0].Content);
        Assert.Equal(2, snap[0].Count);
    }

    [Fact]
    public void Duplicate_Image_By_Hash_Merges_Count_And_Discards_New_File()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"oa-img-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var first = Path.Combine(dir, "first.png");
            var second = Path.Combine(dir, "second.png");
            File.WriteAllBytes(first, [1, 2, 3]);
            File.WriteAllBytes(second, [1, 2, 3]);

            using var store = new ClipboardStore(_tempFile);
            store.AddImage(first, "HASHX");
            store.AddImage(second, "HASHX");

            var snap = store.Snapshot();
            Assert.Single(snap);
            Assert.Equal(2, snap[0].Count);
            Assert.Equal(first, snap[0].ImagePath);
            Assert.False(File.Exists(second)); // 重复内容的新文件被丢弃
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Search_Ranks_Higher_Count_First_On_Tie()
    {
        using var store = new ClipboardStore(_tempFile);
        store.AddText("note alpha");   // 与下行等长，模糊分相同
        store.AddText("note betax");
        store.AddText("note betax");   // betax 计数 2

        var hits = store.Search("note");
        Assert.Equal(2, hits.Count);
        // 同模糊分时，重复次数多的靠前
        Assert.Contains("betax", hits[0].Content);
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
