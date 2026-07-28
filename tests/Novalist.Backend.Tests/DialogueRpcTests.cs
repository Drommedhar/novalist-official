using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class DialogueRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly DialogueRpc _rpc;

    public DialogueRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-dlg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "DlgNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new DialogueRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private EntityService Entities => new(_workspace.Projects);

    private async Task<(ChapterData Chapter, SceneData Scene)> SceneAsync(
        string chapterTitle, string sceneTitle, string html, string date = "")
    {
        var chapter = await _workspace.Projects.CreateChapterAsync(chapterTitle);
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, sceneTitle, date);
        await _workspace.Projects.WriteSceneContentAsync(chapter, scene, html);
        return (chapter, scene);
    }

    private async Task<(string Aldric, string Mira)> CastAsync()
    {
        var aldric = new CharacterData { Name = "Aldric", Surname = "Vane" };
        var mira = new CharacterData { Name = "Mira" };
        await Entities.SaveCharacterAsync(aldric);
        await Entities.SaveCharacterAsync(mira);
        return (aldric.Id, mira.Id);
    }

    [Fact]
    public async Task Index_EmptyBook_ReturnsNothingSelected()
    {
        var result = await _rpc.IndexAsync(null);

        Assert.Empty(result.Speakers);
        Assert.Empty(result.Groups);
        Assert.Equal(0, result.UnassignedCount);
        Assert.Null(result.SelectedId);
    }

    [Fact]
    public async Task Index_GathersLinesGroupedByStoryDate()
    {
        var (aldric, _) = await CastAsync();
        await SceneAsync("One", "Ford", "<p>\"I won't go,\" said Aldric.</p>", "2024-03-01");
        await SceneAsync("Two", "Night", "<p>\"Then stay,\" said Aldric.</p>", "2024-03-11");

        var result = await _rpc.IndexAsync(aldric);

        Assert.Equal(aldric, result.SelectedId);
        Assert.Equal(["2024-03-01", "2024-03-11"], result.Groups.Select(g => g.StoryDate));
        var scene = Assert.Single(result.Groups[0].Scenes);
        Assert.Equal("One", scene.ChapterTitle);
        Assert.Equal("Ford", scene.SceneTitle);
        var line = Assert.Single(scene.Lines);
        Assert.Equal("I won't go,", line.Text);
        Assert.Equal("High", line.Confidence);
        Assert.True(line.Editable);
    }

    [Fact]
    public async Task Index_BlankSelection_OpensOnTheBusiestSpeaker()
    {
        var (aldric, _) = await CastAsync();
        await SceneAsync("One", "S",
            "<p>\"A,\" said Aldric.</p><p>\"B,\" said Aldric.</p><p>\"C,\" said Mira.</p>");

        var result = await _rpc.IndexAsync(null);

        Assert.Equal(aldric, result.SelectedId);
        Assert.Equal(2, result.Speakers[0].LineCount);
    }

    [Fact]
    public async Task Index_ReturnsTheWholeCastForReassignment()
    {
        var (aldric, mira) = await CastAsync();
        // Only Aldric speaks, but Mira still has to be pickable.
        await SceneAsync("One", "S", "<p>\"A,\" said Aldric.</p>");

        var result = await _rpc.IndexAsync(null);

        Assert.Equal(aldric, Assert.Single(result.Speakers).CharacterId);
        Assert.Equal(["Aldric Vane", "Mira"], result.Characters.Select(c => c.Name));
        Assert.Contains(result.Characters, c => c.Id == mira);
    }

    [Fact]
    public async Task Index_SkipsNamelessCharactersInTheCast()
    {
        await CastAsync();
        await Entities.SaveCharacterAsync(new CharacterData { Name = "  ", Surname = "  " });
        await SceneAsync("One", "S", "<p>\"A,\" said Aldric.</p>");

        var result = await _rpc.IndexAsync(null);

        Assert.All(result.Characters, c => Assert.NotEqual(string.Empty, c.Name));
    }

    [Fact]
    public async Task Index_UnassignedSelectionListsOrphanLines()
    {
        await CastAsync();
        await SceneAsync("One", "S", "<p>\"Nobody asked you.\"</p>");

        var result = await _rpc.IndexAsync("?unassigned");

        Assert.Equal(1, result.UnassignedCount);
        Assert.Equal("?unassigned", result.SelectedId);
        Assert.Equal("None", Assert.Single(Assert.Single(Assert.Single(result.Groups).Scenes).Lines).Confidence);
    }

    [Fact]
    public async Task Index_UnassignedSelectionWithNoOrphans_SelectsNothing()
    {
        await CastAsync();
        await SceneAsync("One", "S", "<p>\"A,\" said Aldric.</p>");

        var result = await _rpc.IndexAsync("?unassigned");

        Assert.Null(result.SelectedId);
        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task Index_UnknownCharacter_SelectsNothing()
    {
        await CastAsync();
        await SceneAsync("One", "S", "<p>\"A,\" said Aldric.</p>");

        var result = await _rpc.IndexAsync("ghost");

        Assert.Null(result.SelectedId);
        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task Index_MarksLinesCarryingMarkupAsNotEditable()
    {
        var (aldric, _) = await CastAsync();
        await SceneAsync("One", "S", "<p>\"With <em>stress</em>,\" said Aldric.</p>");

        var result = await _rpc.IndexAsync(aldric);

        Assert.False(Assert.Single(Assert.Single(Assert.Single(result.Groups).Scenes).Lines).Editable);
    }

    [Fact]
    public async Task Index_ConfidentLineCarriesNoCandidates()
    {
        var (aldric, _) = await CastAsync();
        await SceneAsync("One", "S", "<p>\"A,\" said Aldric.</p>");

        var result = await _rpc.IndexAsync(aldric);

        var line = Assert.Single(Assert.Single(Assert.Single(result.Groups).Scenes).Lines);
        Assert.Equal("High", line.Confidence);
        Assert.Empty(line.Candidates);
    }

    [Fact]
    public async Task Index_UncertainLineRanksCandidatesForOneClickCorrection()
    {
        var (aldric, _) = await CastAsync();
        await SceneAsync("One", "S",
            "<p>Aldric and Mira waited.</p><p>\"Not a chance.\" Aldric turned away.</p>");

        var result = await _rpc.IndexAsync(aldric);

        var line = Assert.Single(Assert.Single(Assert.Single(result.Groups).Scenes).Lines);
        Assert.Equal("Medium", line.Confidence);
        Assert.Equal(aldric, line.Candidates[0].CharacterId);
        Assert.Equal(100, line.Candidates.Sum(c => c.Percent));
    }

    [Fact]
    public async Task Index_ResolvesAPronounTagToTheOnlyCharacterItCanMean()
    {
        var aldric = new CharacterData { Name = "Aldric", Gender = "male" };
        var mira = new CharacterData { Name = "Mira", Gender = "female" };
        await Entities.SaveCharacterAsync(aldric);
        await Entities.SaveCharacterAsync(mira);
        await SceneAsync("One", "S",
            "<p>Aldric crossed the yard.</p><p>\"Not a chance,\" he said.</p>");

        var result = await _rpc.IndexAsync(aldric.Id);

        var line = Assert.Single(Assert.Single(Assert.Single(result.Groups).Scenes).Lines);
        Assert.Equal("Inferred", line.Confidence);
    }

    [Fact]
    public async Task Index_KeepsASecondQuoteInOneParagraphWithTheSameSpeaker()
    {
        var (aldric, _) = await CastAsync();
        await SceneAsync("One", "S", "<p>\"One,\" said Aldric. \"Still me.\"</p>");

        var result = await _rpc.IndexAsync(aldric);

        var lines = Assert.Single(Assert.Single(result.Groups).Scenes).Lines;
        Assert.Equal(2, lines.Length);
        Assert.All(lines, l => Assert.Equal("High", l.Confidence));
    }

    [Fact]
    public async Task Index_UsesTheProjectWritingLanguageOverride()
    {
        var german = new CharacterData { Name = "Aldric" };
        await Entities.SaveCharacterAsync(german);
        await SceneAsync("Eins", "S", "<p>„Ich gehe nicht“, sagte Aldric.</p>");

        _workspace.Projects.ProjectSettings.Overrides ??= new SettingsOverrides();
        _workspace.Projects.ProjectSettings.Overrides.AutoReplacementLanguage = "de";
        var result = await _rpc.IndexAsync(german.Id);

        // "sagte" only counts as a speech verb via the German lexicon.
        var line = Assert.Single(Assert.Single(Assert.Single(result.Groups).Scenes).Lines);
        Assert.Equal("High", line.Confidence);
    }

    [Fact]
    public async Task Index_FallsBackToTheGlobalWritingLanguage()
    {
        var (aldric, _) = await CastAsync();
        await SceneAsync("One", "S", "<p>\"A,\" said Aldric.</p>");
        _workspace.Settings.Settings.AutoReplacementLanguage = "en";

        var result = await _rpc.IndexAsync(aldric);

        Assert.Equal("High", Assert.Single(Assert.Single(Assert.Single(result.Groups).Scenes).Lines).Confidence);
    }

    [Fact]
    public async Task SetSpeaker_MovesTheLineToAnotherCharacter()
    {
        var (aldric, mira) = await CastAsync();
        const string html = "<p>\"A,\" said Aldric.</p>";
        var (chapter, scene) = await SceneAsync("One", "S", html);
        var key = DialogueScanner.Scan(html)[0].LineKey;

        Assert.True(await _rpc.SetSpeakerAsync(chapter.Guid, scene.Id, key, mira));

        var reassigned = await _rpc.IndexAsync(mira);
        Assert.Equal("Manual", Assert.Single(Assert.Single(Assert.Single(reassigned.Groups).Scenes).Lines).Confidence);
        Assert.DoesNotContain(reassigned.Speakers, s => s.CharacterId == aldric);
    }

    [Fact]
    public async Task SetSpeaker_NullClearsTheLine()
    {
        await CastAsync();
        const string html = "<p>\"A,\" said Aldric.</p>";
        var (chapter, scene) = await SceneAsync("One", "S", html);
        var key = DialogueScanner.Scan(html)[0].LineKey;

        Assert.True(await _rpc.SetSpeakerAsync(chapter.Guid, scene.Id, key, null));

        var result = await _rpc.IndexAsync("?unassigned");
        Assert.Equal(1, result.UnassignedCount);
    }

    [Fact]
    public async Task SetSpeaker_UnknownLine_ReturnsFalse()
    {
        await CastAsync();
        var (chapter, scene) = await SceneAsync("One", "S", "<p>\"A,\" said Aldric.</p>");

        Assert.False(await _rpc.SetSpeakerAsync(chapter.Guid, scene.Id, "ffff:0", "x"));
    }

    [Fact]
    public async Task ClearSpeaker_RestoresAutomaticAttribution()
    {
        var (aldric, mira) = await CastAsync();
        const string html = "<p>\"A,\" said Aldric.</p>";
        var (chapter, scene) = await SceneAsync("One", "S", html);
        var key = DialogueScanner.Scan(html)[0].LineKey;
        await _rpc.SetSpeakerAsync(chapter.Guid, scene.Id, key, mira);

        Assert.True(await _rpc.ClearSpeakerAsync(chapter.Guid, scene.Id, key));

        var result = await _rpc.IndexAsync(aldric);
        Assert.Equal("High", Assert.Single(Assert.Single(Assert.Single(result.Groups).Scenes).Lines).Confidence);
    }

    [Fact]
    public async Task ClearSpeaker_NothingStored_ReturnsFalse()
    {
        await CastAsync();
        var (chapter, scene) = await SceneAsync("One", "S", "<p>\"A,\" said Aldric.</p>");

        Assert.False(await _rpc.ClearSpeakerAsync(chapter.Guid, scene.Id, "ffff:0"));
    }

    [Fact]
    public async Task UpdateLine_WritesTheEditIntoTheSceneFile()
    {
        var (aldric, _) = await CastAsync();
        const string html = "<p>\"I won't go,\" said Aldric.</p>";
        var (chapter, scene) = await SceneAsync("One", "S", html);
        var key = DialogueScanner.Scan(html)[0].LineKey;

        var result = await _rpc.UpdateLineAsync(
            chapter.Guid, scene.Id, key, "I won't go,", "I am staying,");

        Assert.Equal("Updated", result.Status);
        Assert.NotNull(result.LineKey);
        Assert.Equal(
            "<p>\"I am staying,\" said Aldric.</p>",
            await _workspace.Projects.ReadSceneContentAsync(chapter, scene));

        var reloaded = await _rpc.IndexAsync(aldric);
        Assert.Equal(
            "I am staying,",
            Assert.Single(Assert.Single(Assert.Single(reloaded.Groups).Scenes).Lines).Text);
    }

    [Fact]
    public async Task UpdateLine_TakesASnapshotFirst()
    {
        await CastAsync();
        const string html = "<p>\"Before,\" said Aldric.</p>";
        var (chapter, scene) = await SceneAsync("One", "S", html);
        var key = DialogueScanner.Scan(html)[0].LineKey;

        await _rpc.UpdateLineAsync(chapter.Guid, scene.Id, key, "Before,", "After,");

        var snapshots = await new SnapshotService(_workspace.Projects, _workspace.FileService)
            .ListAsync(scene);
        Assert.Contains("Before,", Assert.Single(snapshots).Content);
    }

    [Fact]
    public async Task UpdateLine_StaleScene_IsRefused()
    {
        await CastAsync();
        const string html = "<p>\"Original,\" said Aldric.</p>";
        var (chapter, scene) = await SceneAsync("One", "S", html);
        var key = DialogueScanner.Scan(html)[0].LineKey;

        var result = await _rpc.UpdateLineAsync(
            chapter.Guid, scene.Id, key, "Something else,", "New,");

        Assert.Equal("Stale", result.Status);
        Assert.Null(result.LineKey);
        Assert.Equal(html, await _workspace.Projects.ReadSceneContentAsync(chapter, scene));
    }

    [Fact]
    public async Task UpdateLine_MarkupBearingLine_IsRefused()
    {
        await CastAsync();
        const string html = "<p>\"With <em>stress</em>,\" said Aldric.</p>";
        var (chapter, scene) = await SceneAsync("One", "S", html);
        var span = DialogueScanner.Scan(html)[0];

        var result = await _rpc.UpdateLineAsync(
            chapter.Guid, scene.Id, span.LineKey, span.Text, "plain");

        Assert.Equal("NotEditable", result.Status);
        Assert.Equal(html, await _workspace.Projects.ReadSceneContentAsync(chapter, scene));
    }

    [Fact]
    public async Task UpdateLine_UnknownScene_IsRefused()
    {
        await CastAsync();

        var result = await _rpc.UpdateLineAsync("nope", "nope", "key", "a", "b");

        Assert.Equal("Stale", result.Status);
    }
}
