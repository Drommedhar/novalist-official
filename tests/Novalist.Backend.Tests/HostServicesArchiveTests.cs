using NSubstitute;
using Novalist.Backend.Extensions;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sdk.Services;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// The project as files and stored versions.
///
/// Run against a real project on disk, because every one of these is a call
/// where "it compiled" and "it read the right draft" are different questions.
/// </summary>
[Collection("BackendStatics")]
public class HostServicesArchiveTests
{
    private static (HostServices Host, ProjectService Proj, SceneEditingState Editing, TempDir Dir) Build()
    {
        var dir = new TempDir();
        var file = new FileService();
        var proj = new ProjectService(file);
        proj.CreateProjectAsync(dir.Path, "P", "Book").GetAwaiter().GetResult();
        var settings = Substitute.For<ISettingsService>();
        settings.Settings.Returns(new AppSettings());
        settings.SaveAsync().Returns(Task.CompletedTask);
        var editing = new SceneEditingState();
        return (new HostServices(file, proj, new EntityService(proj), settings, null, editing),
            proj, editing, dir);
    }

    private static async Task<(string Chapter, string Scene)> SceneAsync(HostServices host, string html)
    {
        var chapter = await host.ProjectService.CreateChapterAsync("One");
        var scene = await host.ProjectService.CreateSceneAsync(chapter, "Arrival");
        await host.ProjectService.WriteSceneContentAsync(chapter, scene, html);
        return (chapter, scene);
    }

    // ── Snapshots ──

    [Fact]
    public async Task ASnapshotCanBeTakenListedReadAndPutBack()
    {
        var (host, _, _, dir) = Build();
        using var _d = dir;
        var (chapter, scene) = await SceneAsync(host, "<p>The bell rang once.</p>");

        var id = await host.ArchiveService.TakeSnapshotAsync(chapter, scene, "before the pass");
        Assert.NotNull(id);

        var list = await host.ArchiveService.ListSnapshotsAsync(chapter, scene);
        Assert.Single(list);
        Assert.Equal("before the pass", list[0].Label);
        Assert.Equal(id, list[0].Id);

        Assert.Contains("The bell rang once.",
            await host.ArchiveService.ReadSnapshotAsync(chapter, scene, id!));

        await host.ProjectService.WriteSceneContentAsync(chapter, scene, "<p>Replaced.</p>");
        Assert.True(await host.ArchiveService.RestoreSnapshotAsync(chapter, scene, id!));
        Assert.Contains("The bell rang once.",
            await host.ProjectService.ReadSceneContentAsync(chapter, scene));
    }

    [Fact]
    public async Task RestoringIsRefusedWhileTheSceneIsOpenWithUnsavedText()
    {
        // The editor holds newer text and would autosave over the restore, so
        // it would not survive anyway - and the writer would lose both.
        var (host, _, editing, dir) = Build();
        using var _d = dir;
        var (chapter, scene) = await SceneAsync(host, "<p>The bell rang once.</p>");
        var id = await host.ArchiveService.TakeSnapshotAsync(chapter, scene, "v1");

        editing.Set(chapter, scene, dirty: true);

        Assert.False(await host.ArchiveService.RestoreSnapshotAsync(chapter, scene, id!));
    }

    [Fact]
    public async Task SnapshotCallsOnASceneThatIsNotThereComeBackEmpty()
    {
        var (host, _, _, dir) = Build();
        using var _d = dir;
        var (chapter, _) = await SceneAsync(host, "<p>Text.</p>");

        Assert.Empty(await host.ArchiveService.ListSnapshotsAsync(chapter, "no-such-scene"));
        Assert.Null(await host.ArchiveService.ReadSnapshotAsync(chapter, "no-such-scene", "x"));
        Assert.Null(await host.ArchiveService.TakeSnapshotAsync(chapter, "no-such-scene", "x"));
        Assert.False(await host.ArchiveService.RestoreSnapshotAsync(chapter, "no-such-scene", "x"));
    }

    [Fact]
    public async Task AnUnknownSnapshotIdIsRefusedRatherThanGuessedAt()
    {
        var (host, _, _, dir) = Build();
        using var _d = dir;
        var (chapter, scene) = await SceneAsync(host, "<p>Text.</p>");

        Assert.Null(await host.ArchiveService.ReadSnapshotAsync(chapter, scene, "no-such-snapshot"));
        Assert.False(await host.ArchiveService.RestoreSnapshotAsync(chapter, scene, "no-such-snapshot"));
    }

    // ── Other drafts ──

    [Fact]
    public async Task ADraftThatIsNotOpenCanStillBeRead()
    {
        // Comparing two drafts is the most obvious reason to have a second one,
        // and it was the one thing an extension could not do.
        var (host, _, _, dir) = Build();
        using var _d = dir;
        var (chapter, scene) = await SceneAsync(host, "<p>The bell rang once.</p>");
        var first = host.ProjectService.ActiveDraftId!;

        var second = await host.ProjectService.CreateDraftAsync("Revision", first);
        Assert.True(await host.ProjectService.SwitchDraftAsync(second));
        await host.ProjectService.WriteSceneContentAsync(chapter, scene, "<p>The bell rang twice.</p>");
        await host.ProjectService.RenameChapterAsync(chapter, "One, again");

        // Standing in the second draft, reading the first. The title tells the
        // two apart: a clone starts identical, so reading the wrong draft would
        // look right if both still said the same thing.
        var chapters = await host.ArchiveService.GetChaptersOfDraftAsync(first);
        Assert.Contains(chapters, c => c.Guid == chapter && c.Title == "One");
        Assert.Contains("once.",
            await host.ArchiveService.ReadSceneOfDraftAsync(first, chapter, scene));

        // And the open draft is still the open one, with its own text.
        Assert.Equal(second, host.ProjectService.ActiveDraftId);
        Assert.Contains("twice.", await host.ProjectService.ReadSceneContentAsync(chapter, scene));
    }

    [Fact]
    public async Task AnUnknownDraftOrChapterOrSceneReadsAsNothing()
    {
        var (host, _, _, dir) = Build();
        using var _d = dir;
        var (chapter, scene) = await SceneAsync(host, "<p>Text.</p>");
        var draft = host.ProjectService.ActiveDraftId!;

        Assert.Empty(await host.ArchiveService.GetChaptersOfDraftAsync("no-such-draft"));
        Assert.Null(await host.ArchiveService.ReadSceneOfDraftAsync("no-such-draft", chapter, scene));
        Assert.Null(await host.ArchiveService.ReadSceneOfDraftAsync(draft, "no-such-chapter", scene));
        Assert.Null(await host.ArchiveService.ReadSceneOfDraftAsync(draft, chapter, "no-such-scene"));
    }

    // ── Project files ──

    [Fact]
    public async Task TheProjectsFilesCanBeEnumeratedAndRead()
    {
        var (host, _, _, dir) = Build();
        using var _d = dir;
        await SceneAsync(host, "<p>The bell rang once.</p>");

        var files = host.ArchiveService.ListProjectFiles();

        Assert.NotEmpty(files);
        // Forward slashes, so a path written here means the same file to
        // something reading the archive elsewhere.
        Assert.All(files, f => Assert.DoesNotContain('\\', f.RelativePath));
        var metadata = files.First(f => f.RelativePath.EndsWith("project.json", StringComparison.Ordinal));
        Assert.True(metadata.SizeBytes > 0);

        var bytes = await host.ArchiveService.ReadProjectFileAsync(metadata.RelativePath);
        Assert.NotNull(bytes);
        Assert.Equal(metadata.SizeBytes, bytes!.Length);
    }

    [Fact]
    public async Task APathThatClimbsOutOfTheProjectIsRefused()
    {
        // Otherwise a call meant for the project's own files reads anything on
        // the machine.
        var (host, _, _, dir) = Build();
        using var _d = dir;

        Assert.Null(await host.ArchiveService.ReadProjectFileAsync("../outside.txt"));
        Assert.Null(await host.ArchiveService.ReadProjectFileAsync("   "));
        Assert.Null(await host.ArchiveService.ReadProjectFileAsync("does-not-exist.json"));
    }

    [Fact]
    public async Task AResearchFilesPathResolvesInsideTheProjectAndNowhereElse()
    {
        var (host, _, _, dir) = Build();
        using var _d = dir;
        await SceneAsync(host, "<p>Text.</p>");

        var resolved = host.ResearchService.GetFullPath(".novalist/project.json");
        Assert.True(File.Exists(resolved));

        Assert.Equal(string.Empty, host.ResearchService.GetFullPath("../outside.txt"));
        Assert.Equal(string.Empty, host.ResearchService.GetFullPath("  "));
    }
}
