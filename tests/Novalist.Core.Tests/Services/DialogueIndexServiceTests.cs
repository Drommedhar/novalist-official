using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Novalist.Core.Utilities;
using NSubstitute;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class DialogueIndexServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProjectService _project = new(new FileService());

    public void Dispose() => _dir.Dispose();

    private static readonly CharacterData Aldric = new() { Id = "hero", Name = "Aldric" };
    private static readonly CharacterData Mira = new() { Id = "mira", Name = "Mira" };
    private static readonly CharacterData[] Cast = [Aldric, Mira];

    private DialogueIndexService Service(ISnapshotService? snapshots = null)
        => new(_project, snapshots);

    private async Task<(ChapterData Chapter, SceneData Scene)> SceneWithAsync(
        string chapterTitle, string sceneTitle, string html, string date = "")
    {
        var chapter = await _project.CreateChapterAsync(chapterTitle);
        var scene = await _project.CreateSceneAsync(chapter.Guid, sceneTitle, date);
        await _project.WriteSceneContentAsync(chapter, scene, html);
        return (chapter, scene);
    }

    private async Task NewProjectAsync() => await _project.CreateProjectAsync(_dir.Path, "P", "Book");

    [Fact]
    public async Task Build_TalliesSpeakersAndSelectsTheBusiest()
    {
        await NewProjectAsync();
        await SceneWithAsync("One", "S",
            "<p>\"A,\" said Aldric.</p><p>\"B,\" said Aldric.</p><p>\"C,\" said Mira.</p>");

        var index = await Service().BuildAsync(Cast, null, "en");

        Assert.Equal(["hero", "mira"], index.Speakers.Select(s => s.CharacterId));
        Assert.Equal(2, index.Speakers[0].LineCount);
        Assert.Equal("Aldric", index.Speakers[0].Name);
        // No explicit pick, so the view opens on whoever talks most.
        Assert.Equal(2, index.Groups.Sum(g => g.Scenes.Sum(s => s.Lines.Count)));
    }

    [Fact]
    public async Task Build_TieOnLineCountBreaksByName()
    {
        await NewProjectAsync();
        await SceneWithAsync("One", "S", "<p>\"A,\" said Mira.</p><p>\"B,\" said Aldric.</p>");

        var index = await Service().BuildAsync(Cast, null, "en");

        Assert.Equal(["Aldric", "Mira"], index.Speakers.Select(s => s.Name));
    }

    [Fact]
    public async Task Build_CountsUnattributedLinesSeparately()
    {
        await NewProjectAsync();
        await SceneWithAsync("One", "S", "<p>\"Nobody asked you.\"</p>");

        var index = await Service().BuildAsync(Cast, null, "en");

        Assert.Equal(1, index.UnassignedCount);
        Assert.Empty(index.Speakers);
        // Nobody speaks, so there is nothing to open on.
        Assert.Empty(index.Groups);
    }

    [Fact]
    public async Task Build_UnassignedSelectionListsTheOrphanLines()
    {
        await NewProjectAsync();
        await SceneWithAsync("One", "S", "<p>\"Nobody asked you.\"</p>");

        var index = await Service().BuildAsync(
            Cast, DialogueIndexService.UnassignedSpeakerId, "en");

        var line = Assert.Single(Assert.Single(Assert.Single(index.Groups).Scenes).Lines);
        Assert.Equal("Nobody asked you.", line.Text);
    }

    [Fact]
    public async Task Build_UnassignedSelectionIsEmpty_WhenEveryLineIsAttributed()
    {
        await NewProjectAsync();
        await SceneWithAsync("One", "S", "<p>\"A,\" said Aldric.</p>");

        var index = await Service().BuildAsync(
            Cast, DialogueIndexService.UnassignedSpeakerId, "en");

        Assert.Empty(index.Groups);
    }

    [Fact]
    public async Task Build_UnknownCharacterSelection_ReturnsNoGroups()
    {
        await NewProjectAsync();
        await SceneWithAsync("One", "S", "<p>\"A,\" said Aldric.</p>");

        var index = await Service().BuildAsync(Cast, "ghost", "en");

        Assert.Empty(index.Groups);
        Assert.Single(index.Speakers);
    }

    [Fact]
    public async Task Build_EmptyBookReturnsNothing()
    {
        await NewProjectAsync();

        var index = await Service().BuildAsync(Cast, null, "en");

        Assert.Empty(index.Speakers);
        Assert.Empty(index.Groups);
        Assert.Equal(0, index.UnassignedCount);
    }

    [Fact]
    public async Task Build_SkipsScenesWithNoDialogue()
    {
        await NewProjectAsync();
        await SceneWithAsync("One", "Narration", "<p>Nothing is spoken here.</p>");
        await SceneWithAsync("Two", "Speech", "<p>\"A,\" said Aldric.</p>");

        var index = await Service().BuildAsync(Cast, "hero", "en");

        var scene = Assert.Single(Assert.Single(index.Groups).Scenes);
        Assert.Equal("Speech", scene.SceneTitle);
    }

    [Fact]
    public async Task Build_GroupsByStoryDate_InReadingOrder()
    {
        await NewProjectAsync();
        await SceneWithAsync("One", "S1", "<p>\"A,\" said Aldric.</p>", "2024-03-01");
        await SceneWithAsync("Two", "S2", "<p>\"B,\" said Aldric.</p>", "2024-03-05");

        var index = await Service().BuildAsync(Cast, "hero", "en");

        Assert.Equal(["2024-03-01", "2024-03-05"], index.Groups.Select(g => g.StoryDate));
    }

    [Fact]
    public async Task Build_ScenesSharingADateStayInOneGroup()
    {
        await NewProjectAsync();
        await SceneWithAsync("One", "S1", "<p>\"A,\" said Aldric.</p>", "2024-03-01");
        await SceneWithAsync("Two", "S2", "<p>\"B,\" said Aldric.</p>", "2024-03-01");

        var index = await Service().BuildAsync(Cast, "hero", "en");

        Assert.Equal(2, Assert.Single(index.Groups).Scenes.Count);
    }

    [Fact]
    public async Task Build_UndatedSceneContinuesThePrecedingGroup()
    {
        await NewProjectAsync();
        await SceneWithAsync("One", "Dated", "<p>\"A,\" said Aldric.</p>", "2024-03-01");
        await SceneWithAsync("Two", "Undated", "<p>\"B,\" said Aldric.</p>");

        var index = await Service().BuildAsync(Cast, "hero", "en");

        var group = Assert.Single(index.Groups);
        Assert.Equal("2024-03-01", group.StoryDate);
        Assert.Equal(["Dated", "Undated"], group.Scenes.Select(s => s.SceneTitle));
    }

    [Fact]
    public async Task Build_UndatedScenesBeforeAnyDateFormTheirOwnLeadingGroup()
    {
        await NewProjectAsync();
        await SceneWithAsync("One", "Undated", "<p>\"A,\" said Aldric.</p>");
        await SceneWithAsync("Two", "Dated", "<p>\"B,\" said Aldric.</p>", "2024-03-01");

        var index = await Service().BuildAsync(Cast, "hero", "en");

        Assert.Equal([string.Empty, "2024-03-01"], index.Groups.Select(g => g.StoryDate));
    }

    [Fact]
    public async Task Build_DropsGroupsWhereTheSelectedSpeakerSaysNothing()
    {
        await NewProjectAsync();
        await SceneWithAsync("One", "S1", "<p>\"A,\" said Mira.</p>", "2024-03-01");
        await SceneWithAsync("Two", "S2", "<p>\"B,\" said Aldric.</p>", "2024-03-05");

        var index = await Service().BuildAsync(Cast, "hero", "en");

        Assert.Equal("2024-03-05", Assert.Single(index.Groups).StoryDate);
    }

    [Fact]
    public async Task Build_CarriesSceneCoordinatesAndLineDetail()
    {
        await NewProjectAsync();
        var (chapter, scene) = await SceneWithAsync(
            "One", "Opening", "<p>\"I won't go,\" said Aldric.</p>", "2024-03-01");

        var index = await Service().BuildAsync(Cast, "hero", "en");
        var listed = Assert.Single(Assert.Single(index.Groups).Scenes);
        var line = Assert.Single(listed.Lines);

        Assert.Equal(chapter.Guid, listed.ChapterGuid);
        Assert.Equal(scene.Id, listed.SceneId);
        Assert.Equal("One", listed.ChapterTitle);
        Assert.Equal("Opening", listed.SceneTitle);
        Assert.Equal("2024-03-01", listed.StoryDate);
        Assert.Equal("I won't go,", line.Text);
        Assert.Equal(DialogueConfidence.High, line.Confidence);
        Assert.True(line.Editable);
        Assert.Equal("said Aldric.", line.ContextAfter);
    }

    [Fact]
    public async Task Build_HonoursStoredSpeakerOverrides()
    {
        await NewProjectAsync();
        var (_, scene) = await SceneWithAsync("One", "S", "<p>\"A,\" said Aldric.</p>");
        var key = DialogueScanner.Scan("<p>\"A,\" said Aldric.</p>")[0].LineKey;
        scene.DialogueSpeakers = new Dictionary<string, string> { [key] = "mira" };

        var index = await Service().BuildAsync(Cast, "mira", "en");

        var line = Assert.Single(Assert.Single(Assert.Single(index.Groups).Scenes).Lines);
        Assert.Equal(DialogueConfidence.Manual, line.Confidence);
    }

    [Fact]
    public async Task Build_LanguageWithoutALexiconStillAttributesByName()
    {
        await NewProjectAsync();
        await SceneWithAsync("One", "S", "<p>\"A,\" said Aldric.</p>");

        // No lexicon for this tag: no speech verbs, so the verdict drops to Medium.
        var index = await Service().BuildAsync(Cast, "hero", "xx-QQ");

        var line = Assert.Single(Assert.Single(Assert.Single(index.Groups).Scenes).Lines);
        Assert.Equal(DialogueConfidence.Medium, line.Confidence);
    }

    // ── Speaker overrides ───────────────────────────────────────────

    [Fact]
    public async Task SetSpeaker_StoresTheOverride()
    {
        await NewProjectAsync();
        var (chapter, scene) = await SceneWithAsync("One", "S", "<p>\"A,\" said Aldric.</p>");
        var key = DialogueScanner.Scan("<p>\"A,\" said Aldric.</p>")[0].LineKey;

        var ok = await Service().SetSpeakerAsync(chapter.Guid, scene.Id, key, "mira");

        Assert.True(ok);
        Assert.Equal("mira", scene.DialogueSpeakers![key]);
    }

    [Fact]
    public async Task SetSpeaker_NullIdClearsTheLineWithoutRemovingTheOverride()
    {
        await NewProjectAsync();
        var (chapter, scene) = await SceneWithAsync("One", "S", "<p>\"A,\" said Aldric.</p>");
        var key = DialogueScanner.Scan("<p>\"A,\" said Aldric.</p>")[0].LineKey;

        await Service().SetSpeakerAsync(chapter.Guid, scene.Id, key, null);

        Assert.Equal(string.Empty, scene.DialogueSpeakers![key]);
    }

    [Fact]
    public async Task SetSpeaker_UnknownScene_ReturnsFalse()
    {
        await NewProjectAsync();
        var (chapter, _) = await SceneWithAsync("One", "S", "<p>\"A,\" said Aldric.</p>");

        Assert.False(await Service().SetSpeakerAsync(chapter.Guid, "nope", "key", "hero"));
    }

    [Fact]
    public async Task SetSpeaker_UnknownChapter_ReturnsFalse()
    {
        await NewProjectAsync();
        var (_, scene) = await SceneWithAsync("One", "S", "<p>\"A,\" said Aldric.</p>");

        Assert.False(await Service().SetSpeakerAsync("nope", scene.Id, "key", "hero"));
    }

    [Fact]
    public async Task SetSpeaker_UnknownLine_ReturnsFalse()
    {
        await NewProjectAsync();
        var (chapter, scene) = await SceneWithAsync("One", "S", "<p>\"A,\" said Aldric.</p>");

        Assert.False(await Service().SetSpeakerAsync(chapter.Guid, scene.Id, "ffff:0", "hero"));
        Assert.Null(scene.DialogueSpeakers);
    }

    [Fact]
    public async Task ClearSpeaker_RemovesTheOverrideAndDropsAnEmptyMap()
    {
        await NewProjectAsync();
        var (chapter, scene) = await SceneWithAsync("One", "S", "<p>\"A,\" said Aldric.</p>");
        var key = DialogueScanner.Scan("<p>\"A,\" said Aldric.</p>")[0].LineKey;
        await Service().SetSpeakerAsync(chapter.Guid, scene.Id, key, "mira");

        var ok = await Service().ClearSpeakerAsync(chapter.Guid, scene.Id, key);

        Assert.True(ok);
        Assert.Null(scene.DialogueSpeakers);
    }

    [Fact]
    public async Task ClearSpeaker_KeepsOtherOverrides()
    {
        await NewProjectAsync();
        const string html = "<p>\"A,\" said Aldric.</p><p>\"B,\" said Mira.</p>";
        var (chapter, scene) = await SceneWithAsync("One", "S", html);
        var spans = DialogueScanner.Scan(html);
        await Service().SetSpeakerAsync(chapter.Guid, scene.Id, spans[0].LineKey, "mira");
        await Service().SetSpeakerAsync(chapter.Guid, scene.Id, spans[1].LineKey, "hero");

        await Service().ClearSpeakerAsync(chapter.Guid, scene.Id, spans[0].LineKey);

        Assert.Equal("hero", Assert.Single(scene.DialogueSpeakers!).Value);
    }

    [Fact]
    public async Task ClearSpeaker_NoOverrideStored_ReturnsFalse()
    {
        await NewProjectAsync();
        var (chapter, scene) = await SceneWithAsync("One", "S", "<p>\"A,\" said Aldric.</p>");

        Assert.False(await Service().ClearSpeakerAsync(chapter.Guid, scene.Id, "ffff:0"));
    }

    [Fact]
    public async Task ClearSpeaker_UnknownScene_ReturnsFalse()
    {
        await NewProjectAsync();

        Assert.False(await Service().ClearSpeakerAsync("nope", "nope", "key"));
    }

    // ── Line edits ──────────────────────────────────────────────────

    [Fact]
    public async Task UpdateLine_RewritesTheSceneFileAndLeavesTheTagIntact()
    {
        await NewProjectAsync();
        const string html = "<p>\"I won't go,\" said Aldric.</p>";
        var (chapter, scene) = await SceneWithAsync("One", "S", html);
        var key = DialogueScanner.Scan(html)[0].LineKey;

        var result = await Service().UpdateLineAsync(
            chapter.Guid, scene.Id, key, "I won't go,", "I am staying,");

        Assert.Equal(DialogueUpdateStatus.Updated, result.Status);
        Assert.Equal(
            "<p>\"I am staying,\" said Aldric.</p>",
            await _project.ReadSceneContentAsync(chapter, scene));
    }

    [Fact]
    public async Task UpdateLine_RecomputesTheSceneWordCount()
    {
        await NewProjectAsync();
        const string html = "<p>\"One two,\" said Aldric.</p>";
        var (chapter, scene) = await SceneWithAsync("One", "S", html);
        var key = DialogueScanner.Scan(html)[0].LineKey;

        await Service().UpdateLineAsync(chapter.Guid, scene.Id, key, "One two,", "One two three four,");

        // "One two three four said Aldric" — five spoken words plus the two-word tag.
        Assert.Equal(6, scene.WordCount);
    }

    [Fact]
    public async Task UpdateLine_TakesASnapshotBeforeWriting()
    {
        await NewProjectAsync();
        const string html = "<p>\"Before,\" said Aldric.</p>";
        var (chapter, scene) = await SceneWithAsync("One", "S", html);
        var key = DialogueScanner.Scan(html)[0].LineKey;
        var snapshots = Substitute.For<ISnapshotService>();

        await Service(snapshots).UpdateLineAsync(chapter.Guid, scene.Id, key, "Before,", "After,");

        await snapshots.Received(1).TakeAsync(chapter, scene, Arg.Any<string>());
    }

    [Fact]
    public async Task UpdateLine_NoSnapshotService_StillWrites()
    {
        await NewProjectAsync();
        const string html = "<p>\"Before,\" said Aldric.</p>";
        var (chapter, scene) = await SceneWithAsync("One", "S", html);
        var key = DialogueScanner.Scan(html)[0].LineKey;

        var result = await Service().UpdateLineAsync(chapter.Guid, scene.Id, key, "Before,", "After,");

        Assert.Equal(DialogueUpdateStatus.Updated, result.Status);
    }

    [Fact]
    public async Task UpdateLine_RefusesWhenTheSceneChangedUnderneath()
    {
        await NewProjectAsync();
        const string html = "<p>\"Original,\" said Aldric.</p>";
        var (chapter, scene) = await SceneWithAsync("One", "S", html);
        var key = DialogueScanner.Scan(html)[0].LineKey;
        // Somebody edited the scene in the editor since the list was built.
        await _project.WriteSceneContentAsync(chapter, scene, "<p>\"Changed,\" said Aldric.</p>");

        var result = await Service().UpdateLineAsync(
            chapter.Guid, scene.Id, key, "Original,", "New words,");

        Assert.Equal(DialogueUpdateStatus.Stale, result.Status);
        Assert.Equal(
            "<p>\"Changed,\" said Aldric.</p>",
            await _project.ReadSceneContentAsync(chapter, scene));
    }

    [Fact]
    public async Task UpdateLine_RefusesWhenTheCallersTextDoesNotMatch()
    {
        await NewProjectAsync();
        const string html = "<p>\"Original,\" said Aldric.</p>";
        var (chapter, scene) = await SceneWithAsync("One", "S", html);
        var key = DialogueScanner.Scan(html)[0].LineKey;

        var result = await Service().UpdateLineAsync(
            chapter.Guid, scene.Id, key, "Something else,", "New words,");

        Assert.Equal(DialogueUpdateStatus.Stale, result.Status);
    }

    [Fact]
    public async Task UpdateLine_RefusesLinesThatCarryMarkup()
    {
        await NewProjectAsync();
        const string html = "<p>\"With <em>stress</em>,\" said Aldric.</p>";
        var (chapter, scene) = await SceneWithAsync("One", "S", html);
        var span = DialogueScanner.Scan(html)[0];

        var result = await Service().UpdateLineAsync(
            chapter.Guid, scene.Id, span.LineKey, span.Text, "plain now");

        Assert.Equal(DialogueUpdateStatus.NotEditable, result.Status);
    }

    [Fact]
    public async Task UpdateLine_UnknownScene_IsStale()
    {
        await NewProjectAsync();

        var result = await Service().UpdateLineAsync("nope", "nope", "key", "a", "b");

        Assert.Equal(DialogueUpdateStatus.Stale, result.Status);
    }

    [Fact]
    public async Task UpdateLine_MovesTheSpeakerOverrideOntoTheNewKey()
    {
        await NewProjectAsync();
        const string html = "<p>\"Before,\" said Aldric.</p>";
        var (chapter, scene) = await SceneWithAsync("One", "S", html);
        var key = DialogueScanner.Scan(html)[0].LineKey;
        await Service().SetSpeakerAsync(chapter.Guid, scene.Id, key, "mira");

        var result = await Service().UpdateLineAsync(chapter.Guid, scene.Id, key, "Before,", "After,");

        Assert.NotEqual(key, result.LineKey);
        Assert.Equal("mira", scene.DialogueSpeakers![result.LineKey!]);
        Assert.False(scene.DialogueSpeakers.ContainsKey(key));
    }

    [Fact]
    public async Task UpdateLine_NewKeyMatchesAFreshScanOfTheSavedScene()
    {
        await NewProjectAsync();
        const string html = "<p>\"Before,\" said Aldric.</p><p>\"After,\" said Mira.</p>";
        var (chapter, scene) = await SceneWithAsync("One", "S", html);
        var spans = DialogueScanner.Scan(html);

        // Edit the first line to read exactly like the second, so the ordinal matters.
        var result = await Service().UpdateLineAsync(
            chapter.Guid, scene.Id, spans[0].LineKey, "Before,", "After,");

        var rescanned = DialogueScanner.Scan(await _project.ReadSceneContentAsync(chapter, scene));
        Assert.Equal(rescanned[0].LineKey, result.LineKey);
    }

    [Fact]
    public async Task UpdateLine_EditingALaterLineToMatchAnEarlierOne_TakesTheSecondOrdinal()
    {
        await NewProjectAsync();
        const string html = "<p>\"Same,\" said Aldric.</p><p>\"Different,\" said Mira.</p>";
        var (chapter, scene) = await SceneWithAsync("One", "S", html);
        var spans = DialogueScanner.Scan(html);

        // The second line now reads exactly like the first, so its key must carry
        // ordinal 1 — otherwise the two lines would collide.
        var result = await Service().UpdateLineAsync(
            chapter.Guid, scene.Id, spans[1].LineKey, "Different,", "Same,");

        var rescanned = DialogueScanner.Scan(await _project.ReadSceneContentAsync(chapter, scene));
        Assert.Equal(rescanned[1].LineKey, result.LineKey);
        Assert.NotEqual(rescanned[0].LineKey, result.LineKey);
    }

    [Fact]
    public async Task UpdateLine_UnchangedKey_LeavesTheOverrideAlone()
    {
        await NewProjectAsync();
        // Only the casing changes, so the normalized key is identical.
        const string html = "<p>\"before,\" said Aldric.</p>";
        var (chapter, scene) = await SceneWithAsync("One", "S", html);
        var key = DialogueScanner.Scan(html)[0].LineKey;
        await Service().SetSpeakerAsync(chapter.Guid, scene.Id, key, "mira");

        var result = await Service().UpdateLineAsync(chapter.Guid, scene.Id, key, "before,", "Before,");

        Assert.Equal(key, result.LineKey);
        Assert.Equal("mira", scene.DialogueSpeakers![key]);
    }

    [Fact]
    public async Task UpdateLine_NoOverrideStored_StillReportsTheNewKey()
    {
        await NewProjectAsync();
        const string html = "<p>\"Before,\" said Aldric.</p>";
        var (chapter, scene) = await SceneWithAsync("One", "S", html);
        var key = DialogueScanner.Scan(html)[0].LineKey;

        var result = await Service().UpdateLineAsync(chapter.Guid, scene.Id, key, "Before,", "After,");

        Assert.NotNull(result.LineKey);
        Assert.Null(scene.DialogueSpeakers);
    }

    [Fact]
    public async Task UpdateLine_OverridesForOtherLinesSurviveTheMigration()
    {
        await NewProjectAsync();
        const string html = "<p>\"A,\" said Aldric.</p><p>\"B,\" said Mira.</p>";
        var (chapter, scene) = await SceneWithAsync("One", "S", html);
        var spans = DialogueScanner.Scan(html);
        await Service().SetSpeakerAsync(chapter.Guid, scene.Id, spans[1].LineKey, "hero");

        await Service().UpdateLineAsync(chapter.Guid, scene.Id, spans[0].LineKey, "A,", "A revised,");

        Assert.Equal("hero", scene.DialogueSpeakers![spans[1].LineKey]);
    }
}
