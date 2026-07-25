using Novalist.Backend;
using Novalist.Sdk.Services;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>Extension host-event raising and lifetime on <see cref="Workspace"/>.</summary>
[Collection("BackendStatics")]
public sealed class WorkspaceEventsTests : IDisposable
{
    private readonly string _root;

    public WorkspaceEventsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-wsev-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private Workspace CreateWorkspace() => new(Path.Combine(_root, "settings"));

    [Fact]
    public void UiBridge_IsAvailable_BeforeHostCreated()
    {
        using var ws = CreateWorkspace();
        Assert.NotNull(ws.UiBridge);
        Assert.Null(ws.HostServices); // not created until an extension is touched
    }

    [Fact]
    public void Dispose_NoHost_DoesNotThrow()
    {
        var ws = CreateWorkspace();
        ws.Dispose();
        ws.Dispose(); // idempotent
    }

    [Fact]
    public void Raisers_NoHost_AreNoOps()
    {
        using var ws = CreateWorkspace();
        ws.RaiseProjectLoaded();
        ws.RaiseBookChanged();
        ws.RaiseSceneOpened(new Novalist.Core.Models.ChapterData { Guid = "c", Title = "C" },
            new Novalist.Core.Models.SceneData { Id = "s", Title = "S" });
        ws.RaiseSceneSaved(new Novalist.Core.Models.ChapterData { Guid = "c" },
            new Novalist.Core.Models.SceneData { Id = "s" });
        ws.RaiseLanguageChanged("de"); // still updates the extension-facing language
        Assert.Equal("de", Novalist.Backend.Extensions.Loc.Instance.CurrentLanguage);
    }

    [Fact]
    public void Raisers_WithHost_ButNoProject_EarlyReturn()
    {
        using var ws = CreateWorkspace();
        _ = ws.ExtensionsHost; // create the host
        ws.RaiseProjectLoaded(); // CurrentProject null -> early return
        ws.RaiseBookChanged();   // ActiveBook null -> early return
        Assert.NotNull(ws.HostServices);
    }

    [Fact]
    public async Task Raisers_WithHostAndProject_FireEvents()
    {
        using var ws = CreateWorkspace();
        var host = ws.ExtensionsHost.Host; // create host BEFORE opening so ProjectLoaded is observed

        ProjectInfo? loaded = null;
        SceneInfo? opened = null, saved = null;
        BookInfo? book = null;
        string? language = null;
        host.ProjectLoaded += i => loaded = i;
        host.SceneOpened += i => opened = i;
        host.SceneSaved += i => saved = i;
        host.BookChanged += i => book = i;
        host.LanguageChanged += l => language = l;

        await ws.Projects.CreateProjectAsync(_root, "Novel", "Book One");
        await ws.OpenProjectAsync(ws.Projects.ProjectRoot!); // RaiseProjectLoaded
        Assert.Equal("Novel", loaded!.Name);

        var chapter = await ws.Projects.CreateChapterAsync("Chapter One");
        var scene = await ws.Projects.CreateSceneAsync(chapter.Guid, "Opening");

        ws.RaiseSceneOpened(chapter, scene);
        Assert.Equal(scene.Id, opened!.Id);
        ws.RaiseSceneSaved(chapter, scene);
        Assert.NotNull(saved);
        ws.RaiseBookChanged();
        Assert.NotNull(book);
        ws.RaiseLanguageChanged("de");
        Assert.Equal("de", language);
        Assert.Equal("de", Novalist.Backend.Extensions.Loc.Instance.CurrentLanguage);
    }

    [Fact]
    public async Task SyncExtensionLanguage_UsesEffectiveSetting()
    {
        using var ws = CreateWorkspace();
        await ws.Settings.LoadAsync();
        ws.Settings.Settings.Language = "zh-CN";
        ws.SyncExtensionLanguage();
        Assert.Equal("zh-CN", Novalist.Backend.Extensions.Loc.Instance.CurrentLanguage);
    }
}
