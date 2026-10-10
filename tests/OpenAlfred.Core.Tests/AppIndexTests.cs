using OpenAlfred.Core.Apps;
using OpenAlfred.Core.Clipboard;
using OpenAlfred.Core.Files;
using Xunit;

namespace OpenAlfred.Core.Tests;

public class AppIndexTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"oa-apps-{Guid.NewGuid():N}");

    private string MakeShortcut(string dir, string name)
    {
        var full = Path.Combine(_root, dir, name + ".lnk");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "fake shortcut");
        return full;
    }

    [Fact]
    public void Builds_Index_From_StartMenu()
    {
        MakeShortcut("", "notepad");
        MakeShortcut("Visual Studio Code", "Visual Studio Code");

        var index = new AppIndex();
        index.Build([_root]);

        Assert.Equal(2, index.Count);
    }

    [Fact]
    public void Category_Comes_From_Subfolder()
    {
        MakeShortcut("Accessories", "calc");
        var index = new AppIndex();
        index.Build([_root]);

        var entry = index.Search("calc").Single();
        Assert.Equal("calc", entry.Name);
        Assert.Equal("Accessories", entry.Category);
    }

    [Fact]
    public void Startup_Folder_Is_Skipped()
    {
        MakeShortcut("StartUp", "some-app");
        var index = new AppIndex();
        index.Build([_root]);

        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void Non_Shortcut_Files_Are_Ignored()
    {
        MakeShortcut("", "chrome");
        File.WriteAllText(Path.Combine(_root, "readme.txt"), "x");
        var index = new AppIndex();
        index.Build([_root]);

        Assert.Single(index.Search("chrome"));
        Assert.Empty(index.Search("readme"));
    }

    [Fact]
    public void Prefix_Match_Ranks_Best()
    {
        MakeShortcut("", "code");
        MakeShortcut("", "visual-studio-code-insiders");
        var index = new AppIndex();
        index.Build([_root]);

        var results = index.Search("code");
        Assert.Equal("code", results[0].Name);
    }

    [Fact]
    public async Task App_Keyword_Routes_To_AppProvider()
    {
        MakeShortcut("", "calculator");
        var appIndex = new AppIndex();
        appIndex.Build([_root]);

        using var store = new ClipboardStore(Path.Combine(Path.GetTempPath(), $"oa-{Guid.NewGuid():N}.json"));
        using var fileIndex = new FileIndex();
        var router = new QueryRouter(new IQueryProvider[]
        {
            new AppProvider(appIndex),
            new ClipboardProvider(store),
            new FileProvider(fileIndex),
        });

        var results = await router.RouteAsync(">app calc");
        Assert.Equal("app", results[0].Source);
        Assert.Equal("calculator", results[0].Title);
        Assert.Equal(ResultActionKind.OpenPath, results[0].Action);
    }

    [Fact]
    public async Task Bare_Input_Ranks_Apps_Above_Files()
    {
        MakeShortcut("", "spotify");
        var appIndex = new AppIndex();
        appIndex.Build([_root]);

        // 造一个同名文件条目：直接反射注入 FileIndex 不可行，改为验证 app 结果分数 > 100（文件上限）
        using var store = new ClipboardStore(Path.Combine(Path.GetTempPath(), $"oa-{Guid.NewGuid():N}.json"));
        using var fileIndex = new FileIndex();
        var router = new QueryRouter(new IQueryProvider[]
        {
            new AppProvider(appIndex),
            new ClipboardProvider(store),
            new FileProvider(fileIndex),
        });

        var results = await router.RouteAsync("spotify");
        Assert.Contains(results, r => r.Source == "app" && r.Score > 100);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
