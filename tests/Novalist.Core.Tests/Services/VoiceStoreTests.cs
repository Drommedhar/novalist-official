using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Covers the voices a book has been given: that the audio is what is stored,
/// that a voice this machine does not have is a gap rather than a fault, and
/// that Git is told the reference audio is binary before it rewrites bytes
/// inside a WAV.
/// </summary>
public class VoiceStoreTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FileService _files = new();
    private readonly ProjectService _project;

    public VoiceStoreTests() => _project = new ProjectService(_files);

    public void Dispose() => _dir.Dispose();

    private VoiceStore Store() => new(_project, _files);

    private async Task NewProjectAsync()
        => await _project.CreateProjectAsync(_dir.Path, "P", "Book");

    private string VoicesDir()
        => Path.Combine(_project.ProjectRoot!, ".novalist", "narration", "voices");

    private static DesignedVoice Voice(string id = "v1", string format = "wav") => new(
        id, "Mira Vance", "Age: 34. Build: wiry.", "com.example.engine", format, 24000,
        "2026-08-18T10:00:00Z", ReferenceText: "This is the exact reference.");

    private static byte[] Audio() => [0x52, 0x49, 0x46, 0x46, 0x00, 0x01, 0x02, 0x03];

    [Fact]
    public async Task ListAsync_NoProjectOpenHasNoVoices()
        => Assert.Empty(await Store().ListAsync());

    [Fact]
    public async Task ListAsync_NoneDesignedYetHasNoVoices()
    {
        await NewProjectAsync();

        Assert.Empty(await Store().ListAsync());
    }

    [Fact]
    public async Task ListAsync_AnUnreadableIndexIsWorthNoMoreThanAMissingOne()
    {
        await NewProjectAsync();
        Directory.CreateDirectory(VoicesDir());
        await File.WriteAllTextAsync(Path.Combine(VoicesDir(), "voices.json"), "{ not json");

        Assert.Empty(await Store().ListAsync());
    }

    [Fact]
    public async Task SaveAsync_KeepsTheAudioAndTheBriefThatAskedForIt()
    {
        await NewProjectAsync();

        Assert.True(await Store().SaveAsync(Voice(), Audio()));

        var stored = Assert.Single(await Store().ListAsync());
        Assert.Equal("v1", stored.VoiceId);
        Assert.Equal("Mira Vance", stored.DisplayName);
        Assert.Equal("com.example.engine", stored.EngineId);
        Assert.Equal(24000, stored.SampleRate);
        Assert.Equal("This is the exact reference.", stored.ReferenceText);
        Assert.Equal(Audio(), await Store().ReadAudioAsync("v1"));
        Assert.True(File.Exists(Path.Combine(VoicesDir(), "v1.wav")));
    }

    [Fact]
    public async Task SaveAsync_TellsGitTheAudioIsBinary()
    {
        // Without this a checkout with autocrlf on rewrites bytes inside a WAV
        // and the voice comes back as noise, on somebody else's machine, long
        // after the commit that did it.
        await NewProjectAsync();

        await Store().SaveAsync(Voice(), Audio());

        var attributes = await File.ReadAllTextAsync(Path.Combine(VoicesDir(), ".gitattributes"));
        Assert.Contains("*.wav binary", attributes);
    }

    [Fact]
    public async Task SaveAsync_WithNoProjectOpenStoresNothing()
    {
        Assert.False(await Store().SaveAsync(Voice(), Audio()));

        Assert.Empty(Directory.GetFileSystemEntries(_dir.Path));
    }

    [Fact]
    public async Task SaveAsync_ReplacingAVoiceLeavesOneOfIt()
    {
        await NewProjectAsync();
        await Store().SaveAsync(Voice(), Audio());

        await Store().SaveAsync(Voice() with { Description = "Age: 40." }, [0x09]);

        var stored = Assert.Single(await Store().ListAsync());
        Assert.Equal("Age: 40.", stored.Description);
        Assert.Equal([0x09], await Store().ReadAudioAsync("v1"));
    }

    [Fact]
    public async Task SaveAsync_KeepsTheVoicesAlreadyDesigned()
    {
        await NewProjectAsync();
        await Store().SaveAsync(Voice("v1"), Audio());

        await Store().SaveAsync(Voice("v2"), Audio());

        Assert.Equal(["v1", "v2"], (await Store().ListAsync()).Select(v => v.VoiceId));
    }

    [Fact]
    public async Task GetAsync_AVoiceThisBookDoesNotHaveIsNull()
    {
        await NewProjectAsync();

        Assert.Null(await Store().GetAsync("nobody"));
    }

    [Fact]
    public async Task ReadAudioAsync_AVoiceThisMachineDoesNotHaveIsAGapNotAFault()
    {
        // A cast assembled elsewhere: the index names the voice and the audio
        // never arrived.
        await NewProjectAsync();
        await Store().SaveAsync(Voice(), Audio());
        File.Delete(Path.Combine(VoicesDir(), "v1.wav"));

        Assert.Null(await Store().ReadAudioAsync("v1"));
        Assert.Null(await Store().ReadAudioAsync("never-designed"));
    }

    [Fact]
    public async Task ReadAudioAsync_WithNoProjectOpenIsNull()
        => Assert.Null(await Store().ReadAudioAsync("v1"));

    [Fact]
    public async Task ReadAudioForAsync_GathersWhatItHasAndLeavesOutWhatItDoesNot()
    {
        await NewProjectAsync();
        await Store().SaveAsync(Voice("v1"), Audio());

        var audio = await Store().ReadAudioForAsync(["v1", "v1", "missing", "", "  "]);

        Assert.Equal(["v1"], audio.Keys);
        Assert.Equal(Audio(), audio["v1"]);
    }

    [Fact]
    public async Task ReadReferenceTextsForAsync_GathersOnlyExactTranscripts()
    {
        await NewProjectAsync();
        await Store().SaveAsync(Voice("v1"), Audio());
        await Store().SaveAsync(Voice("old") with { ReferenceText = string.Empty }, Audio());

        var texts = await Store().ReadReferenceTextsForAsync(["v1", "old", "missing"]);

        Assert.Equal(["v1"], texts.Keys);
        Assert.Equal("This is the exact reference.", texts["v1"]);
    }

    [Fact]
    public async Task DeleteAsync_ForgetsTheVoiceAndItsAudio()
    {
        await NewProjectAsync();
        await Store().SaveAsync(Voice("v1"), Audio());
        await Store().SaveAsync(Voice("v2"), Audio());

        Assert.True(await Store().DeleteAsync("v1"));

        Assert.Equal(["v2"], (await Store().ListAsync()).Select(v => v.VoiceId));
        Assert.False(File.Exists(Path.Combine(VoicesDir(), "v1.wav")));
    }

    [Fact]
    public async Task DeleteAsync_SaysWhenThereWasNothingToForget()
    {
        await NewProjectAsync();

        Assert.False(await Store().DeleteAsync("never-designed"));
        Assert.False(await Store().DeleteAsync("v1"));
    }

    [Fact]
    public async Task DeleteAsync_WithTheAudioAlreadyGoneStillForgetsTheEntry()
    {
        await NewProjectAsync();
        await Store().SaveAsync(Voice("v1"), Audio());
        File.Delete(Path.Combine(VoicesDir(), "v1.wav"));

        Assert.True(await Store().DeleteAsync("v1"));

        Assert.Empty(await Store().ListAsync());
    }

    [Fact]
    public async Task ReadAudioAsync_AFileSomethingElseIsHoldingIsAGapNotACrash()
    {
        // A backup tool or a scanner with the file open. The reading should lose
        // one voice, not fall over.
        await NewProjectAsync();
        await Store().SaveAsync(Voice(), Audio());

        using var hold = new FileStream(
            Path.Combine(VoicesDir(), "v1.wav"), FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Null(await Store().ReadAudioAsync("v1"));
    }

    [Theory]
    [InlineData("mp3", "v1.mp3")]
    [InlineData(".OPUS", "v1.opus")]
    // Anything that is not a plain container name is written as a wav rather
    // than as a file the operating system will not open.
    [InlineData("", "v1.wav")]
    [InlineData("../escape", "v1.wav")]
    public async Task SaveAsync_NamesTheAudioAfterItsContainer(string format, string expected)
    {
        await NewProjectAsync();

        await Store().SaveAsync(Voice(format: format), Audio());

        Assert.True(File.Exists(Path.Combine(VoicesDir(), expected)));
    }
}
