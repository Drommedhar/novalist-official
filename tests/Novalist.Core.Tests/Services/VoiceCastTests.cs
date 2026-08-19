using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Covers the book's cast sheet: which voice reads whom, kept in the project so
/// it travels with the folder, and always resolving to something rather than to
/// silence.
/// </summary>
public class VoiceCastTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FileService _files = new();
    private readonly ProjectService _project;

    public VoiceCastTests() => _project = new ProjectService(_files);

    public void Dispose() => _dir.Dispose();

    private VoiceCast Cast() => new(_project, _files);

    private async Task NewProjectAsync()
        => await _project.CreateProjectAsync(_dir.Path, "P", "Book");

    private string CastPath()
        => Path.Combine(_project.ProjectRoot!, ".novalist", "narration", "cast.json");

    [Fact]
    public async Task ReadAsync_NoProjectOpenIsAnEmptyCast()
    {
        var sheet = await Cast().ReadAsync();

        Assert.Null(sheet.NarratorVoiceId);
        Assert.Empty(sheet.Voices);
    }

    [Fact]
    public async Task ReadAsync_NoCastAssembledYetIsAnEmptyCast()
    {
        await NewProjectAsync();

        var sheet = await Cast().ReadAsync();

        Assert.Null(sheet.NarratorVoiceId);
        Assert.Empty(sheet.Voices);
    }

    [Fact]
    public async Task ReadAsync_AnUnreadableCastIsWorthNoMoreThanAMissingOne()
    {
        // The voices are re-pickable in a few clicks, so a corrupt sheet is
        // never worth an error in the writer's face.
        await NewProjectAsync();
        Directory.CreateDirectory(Path.GetDirectoryName(CastPath())!);
        await File.WriteAllTextAsync(CastPath(), "{ not json");

        var sheet = await Cast().ReadAsync();

        Assert.Null(sheet.NarratorVoiceId);
        Assert.Empty(sheet.Voices);
    }

    [Fact]
    public async Task WriteAsync_RoundTripsThroughTheProject()
    {
        await NewProjectAsync();
        var written = new VoiceCastSheet
        {
            NarratorVoiceId = "narrator-voice",
            Voices = { ["mira"] = "mira-voice" }
        };

        await Cast().WriteAsync(written);
        var read = await Cast().ReadAsync();

        Assert.Equal("narrator-voice", read.NarratorVoiceId);
        Assert.Equal("mira-voice", read.Voices["mira"]);
        Assert.True(File.Exists(CastPath()));
    }

    [Fact]
    public async Task WriteAsync_NoProjectOpenWritesNothing()
    {
        await Cast().WriteAsync(new VoiceCastSheet { NarratorVoiceId = "v" });

        Assert.Empty(Directory.GetFileSystemEntries(_dir.Path));
    }

    [Fact]
    public async Task SetVoiceAsync_CastsTheNarratorWhenNoCharacterIsNamed()
    {
        await NewProjectAsync();

        var sheet = await Cast().SetVoiceAsync(null, " narrator-voice ");

        Assert.Equal("narrator-voice", sheet.NarratorVoiceId);
        Assert.Equal("narrator-voice", (await Cast().ReadAsync()).NarratorVoiceId);
    }

    [Fact]
    public async Task SetVoiceAsync_CastsACharacter()
    {
        await NewProjectAsync();

        var sheet = await Cast().SetVoiceAsync("mira", "mira-voice");

        Assert.Equal("mira-voice", sheet.Voices["mira"]);
    }

    [Fact]
    public async Task SetVoiceAsync_ABlankVoiceUnCastsRatherThanSilences()
    {
        await NewProjectAsync();
        await Cast().SetVoiceAsync(null, "narrator-voice");
        await Cast().SetVoiceAsync("mira", "mira-voice");

        var sheet = await Cast().SetVoiceAsync("mira", "  ");

        Assert.DoesNotContain("mira", sheet.Voices.Keys);
        // Their lines go back to the narrator, which is still cast.
        Assert.Equal("narrator-voice", VoiceCast.Resolve(sheet, "mira"));
    }

    [Fact]
    public async Task SetVoiceAsync_ABlankVoiceClearsTheNarrator()
    {
        await NewProjectAsync();
        await Cast().SetVoiceAsync(null, "narrator-voice");

        var sheet = await Cast().SetVoiceAsync(null, null);

        Assert.Null(sheet.NarratorVoiceId);
    }

    // ─── a voice that changes with the story ───

    private static VoiceCastSheet Aged()
    {
        var sheet = new VoiceCastSheet { NarratorVoiceId = "narrator-voice" };
        sheet.Voices["mira"] = "mira-voice";
        sheet.Overrides.Add(new VoiceOverride
        {
            CharacterId = "mira", Act = "Three", VoiceId = "mira-act-three"
        });
        sheet.Overrides.Add(new VoiceOverride
        {
            CharacterId = "mira", Chapter = "ch-20", VoiceId = "mira-at-sixty"
        });
        sheet.Overrides.Add(new VoiceOverride
        {
            CharacterId = "mira", Chapter = "ch-20", Scene = "The wall",
            VoiceId = "mira-whispering"
        });
        return sheet;
    }

    [Fact]
    public void Resolve_TheNarrowestThingTheWriterSaidWins()
    {
        var sheet = Aged();

        // Scene beats chapter beats act beats the standing voice - the same
        // precedence the Codex resolves an entry's own fields by, because it is
        // the same statement about the same story.
        Assert.Equal("mira-whispering", VoiceCast.Resolve(
            sheet, "mira", new NarrationPlacement("Three", "ch-20", "The harbour", "The wall")));
        Assert.Equal("mira-at-sixty", VoiceCast.Resolve(
            sheet, "mira", new NarrationPlacement("Three", "ch-20", "The harbour", "Elsewhere")));
        Assert.Equal("mira-act-three", VoiceCast.Resolve(
            sheet, "mira", new NarrationPlacement("Three", "ch-04", "Early", "A scene")));
        Assert.Equal("mira-voice", VoiceCast.Resolve(
            sheet, "mira", new NarrationPlacement("One", "ch-01", "Opening", "A scene")));
    }

    [Fact]
    public void Resolve_AnOverrideIsMatchedByChapterTitleAsWellAsByGuid()
    {
        // Written by the app against the guid, and by a writer editing the file
        // against the title. Both are the same chapter.
        var sheet = new VoiceCastSheet { NarratorVoiceId = "narrator-voice" };
        sheet.Voices["mira"] = "mira-voice";
        sheet.Overrides.Add(new VoiceOverride
        {
            CharacterId = "mira", Chapter = "The harbour wall", VoiceId = "mira-at-sixty"
        });

        Assert.Equal("mira-at-sixty", VoiceCast.Resolve(
            sheet, "mira", new NarrationPlacement(null, "ch-20", "The harbour wall", "A scene")));
    }

    [Fact]
    public void Resolve_WithNoPlaceToStandTheStandingVoiceIsTheAnswer()
    {
        // Every caller that has no position to give passes none, and gets what
        // the reading always was.
        Assert.Equal("mira-voice", VoiceCast.Resolve(Aged(), "mira"));
    }

    [Fact]
    public void Resolve_AnOverrideForSomebodyElseIsNotYours()
    {
        var sheet = Aged();
        Assert.Equal("narrator-voice", VoiceCast.Resolve(
            sheet, "aldric", new NarrationPlacement("Three", "ch-20", "The harbour", "The wall")));
    }

    [Fact]
    public void Resolve_TheNarratorCanChangeToo()
    {
        // A blank character id is the narrator's, so a book that changes hands
        // partway - a framing device, a second narrator - can say so.
        var sheet = new VoiceCastSheet { NarratorVoiceId = "narrator-voice" };
        sheet.Overrides.Add(new VoiceOverride { Chapter = "ch-20", VoiceId = "the-other-teller" });

        Assert.Equal("the-other-teller", VoiceCast.Resolve(
            sheet, null, new NarrationPlacement(null, "ch-20", "Twenty", "A scene")));
        Assert.Equal("narrator-voice", VoiceCast.Resolve(
            sheet, null, new NarrationPlacement(null, "ch-01", "One", "A scene")));
    }

    [Fact]
    public void Resolve_AnOverrideNamingNoVoiceIsNotAnOverride()
    {
        var sheet = new VoiceCastSheet { NarratorVoiceId = "narrator-voice" };
        sheet.Voices["mira"] = "mira-voice";
        sheet.Overrides.Add(new VoiceOverride { CharacterId = "mira", Chapter = "ch-20" });

        Assert.Equal("mira-voice", VoiceCast.Resolve(
            sheet, "mira", new NarrationPlacement(null, "ch-20", "Twenty", "A scene")));
    }

    [Fact]
    public void AllVoices_NamesEveryVoiceTheCastMentionsOnce()
    {
        var voices = VoiceCast.AllVoices(Aged());

        Assert.Contains("mira-voice", voices);
        Assert.Contains("mira-at-sixty", voices);
        Assert.Contains("narrator-voice", voices);
        Assert.Equal(voices.Count, voices.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AllVoices_ACastWithNothingInItNamesNothing()
        => Assert.Empty(VoiceCast.AllVoices(new VoiceCastSheet()));

    [Fact]
    public void Resolve_ACharactersOwnVoiceWinsOverTheNarrators()
    {
        var sheet = new VoiceCastSheet
        {
            NarratorVoiceId = "narrator-voice",
            Voices = { ["mira"] = "mira-voice" }
        };

        Assert.Equal("mira-voice", VoiceCast.Resolve(sheet, "mira"));
    }

    [Fact]
    public void Resolve_AnUncastCharacterIsReadByTheNarrator()
    {
        var sheet = new VoiceCastSheet { NarratorVoiceId = "narrator-voice" };

        // A half-assembled cast produces a complete reading with some of it in
        // the wrong voice, which the writer can hear and fix.
        Assert.Equal("narrator-voice", VoiceCast.Resolve(sheet, "aldric"));
        Assert.Equal("narrator-voice", VoiceCast.Resolve(sheet, null));
    }

    [Fact]
    public void Resolve_NothingCastAtAllResolvesToNothing()
    {
        Assert.Null(VoiceCast.Resolve(new VoiceCastSheet(), "mira"));
    }

    // ── Casting one stretch of the book ──

    [Fact]
    public async Task SetScope_CastsOneActAndLeavesTheStandingVoiceAlone()
    {
        await NewProjectAsync();
        await Cast().SetVoiceAsync("mira", "mira-voice");

        Assert.True(await Cast().SetScopeAsync("mira", new VoiceScope("Two", null, null), "older"));

        var sheet = await Cast().ReadAsync();
        Assert.Equal("mira-voice", VoiceCast.Resolve(sheet, "mira"));
        Assert.Equal(
            "older",
            VoiceCast.Resolve(sheet, "mira", new NarrationPlacement("Two", null, null, null)));
    }

    [Fact]
    public async Task SetScope_TheSameStretchTwiceIsOneOverride()
    {
        await NewProjectAsync();

        await Cast().SetScopeAsync("mira", new VoiceScope("Two", null, null), "older");
        // Retyped, with the whitespace and the capitals a person actually types.
        await Cast().SetScopeAsync("mira", new VoiceScope(" two ", null, null), "oldest");

        var sheet = await Cast().ReadAsync();
        Assert.Equal("oldest", Assert.Single(sheet.Overrides).VoiceId);
    }

    [Fact]
    public async Task SetScope_WithNoVoiceClearsTheStretch()
    {
        await NewProjectAsync();
        await Cast().SetScopeAsync("mira", new VoiceScope("Two", null, null), "older");

        await Cast().SetScopeAsync("mira", new VoiceScope("Two", null, null), "  ");

        Assert.Empty((await Cast().ReadAsync()).Overrides);
    }

    [Fact]
    public async Task SetScope_NamingNowhereIsRefused()
    {
        await NewProjectAsync();

        // An override with nothing set matches every line in the book, which is
        // the standing voice wearing a disguise - and would beat the real one.
        Assert.False(await Cast().SetScopeAsync("mira", new VoiceScope(null, "  ", null), "older"));
        Assert.Empty((await Cast().ReadAsync()).Overrides);
    }

    [Fact]
    public async Task SetScope_TheNarratorGetsThemToo()
    {
        await NewProjectAsync();

        await Cast().SetScopeAsync(null, new VoiceScope(null, "Nine", null), "the-boy");

        var sheet = await Cast().ReadAsync();
        Assert.Equal(string.Empty, Assert.Single(sheet.Overrides).CharacterId);
        Assert.Equal(
            "the-boy",
            VoiceCast.Resolve(sheet, null, new NarrationPlacement(null, null, "Nine", null)));
    }

    [Fact]
    public void ScopedVoiceId_IsDifferentPerStretchAndTheSameForOneOfThem()
    {
        // The bug this exists to end: a second design for the same character
        // reused the first's id and silently overwrote it, so asking for an
        // older Mira in Act Three destroyed how she sounded in Act One.
        var actTwo = VoiceCast.ScopedVoiceId("mira", "eng", new VoiceScope("Two", null, null));
        var actThree = VoiceCast.ScopedVoiceId("mira", "eng", new VoiceScope("Three", null, null));

        Assert.NotEqual(actTwo, actThree);
        Assert.Equal(
            actTwo, VoiceCast.ScopedVoiceId("mira", "eng", new VoiceScope(" TWO ", null, null)));
    }

    [Fact]
    public void ScopedVoiceId_NamingNowhereIsTheStandingId()
        => Assert.Equal(
            "mira-eng",
            VoiceCast.ScopedVoiceId("mira", "eng", new VoiceScope(null, null, null)));

    [Fact]
    public void ScopedVoiceId_SurvivesAChapterTitleWithPunctuationInIt()
    {
        // Voice audio is stored in a file named for its id, and there is no
        // platform on which "Part Two: The Crossing/Return" is a file name.
        var id = VoiceCast.ScopedVoiceId(
            "mira", "eng", new VoiceScope(null, "Part Two: The Crossing/Return", null));

        Assert.DoesNotContain(id, c => Path.GetInvalidFileNameChars().Contains(c));
    }

    [Fact]
    public void ScopedVoiceId_DistinguishesAnActFromAChapterOfTheSameName()
    {
        Assert.NotEqual(
            VoiceCast.ScopedVoiceId("mira", "eng", new VoiceScope("Two", null, null)),
            VoiceCast.ScopedVoiceId("mira", "eng", new VoiceScope(null, "Two", null)));
    }

    [Fact]
    public async Task SetScope_WithNoProjectOpenChangesNothing()
    {
        // Nowhere to write it. Reporting the scope as set would leave a writer
        // believing a cast that does not exist.
        Assert.True(await Cast().SetScopeAsync("mira", new VoiceScope("Two", null, null), "older"));
        Assert.Empty((await Cast().ReadAsync()).Overrides);
    }
}
