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
}
