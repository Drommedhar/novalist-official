using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Covers turning rendered chapters into something a person can play.
///
/// The encoder is faked throughout, because what matters here is what it is
/// asked to do - the chapter marks, the tags, the cover, the order - and what
/// happens on a machine that has no encoder at all, which is the case that has
/// to not throw away a night of rendering.
/// </summary>
public class AudiobookPackagerTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "nl-book-" + Guid.NewGuid().ToString("N"));

    private string Rendered => Path.Combine(_folder, "rendered");
    private string Output => Path.Combine(_folder, "out");

    public AudiobookPackagerTests()
    {
        Directory.CreateDirectory(Rendered);
        Directory.CreateDirectory(Output);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>An encoder that is there, or is not, and remembers what it was asked.</summary>
    private sealed class FakeEncoder(bool available = true, int exitCode = 0) : IMediaEncoder
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<bool> AvailableAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(available);

        public Task<(int ExitCode, string Output)> RunAsync(
            IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
        {
            Calls.Add(arguments);
            return Task.FromResult((exitCode, string.Empty));
        }
    }

    private NarrationChapterAudio Chapter(string title, double durationMs = 60_000)
    {
        var file = $"chapter-{title}.wav";
        File.WriteAllBytes(Path.Combine(Rendered, file), [1, 2, 3]);
        return new NarrationChapterAudio(title, title, file, durationMs, 0, false);
    }

    private static AudiobookMetadata Book(string cover = "")
        => new()
        {
            Title = "The Quiet House",
            Author = "A Writer",
            Description = "A book.",
            Language = "en",
            Year = "2026",
            CoverPath = cover
        };

    // ─── with an encoder ────────────────────────────────────────────

    [Fact]
    public async Task M4bIsOneFile()
    {
        var encoder = new FakeEncoder();

        var result = await new AudiobookPackager(encoder).PackageAsync(
            Rendered, [Chapter("One"), Chapter("Two")], AudiobookFormat.M4b,
            Path.Combine(Output, "book.m4b"), Book());

        Assert.Equal(AudiobookFormat.M4b, result.Format);
        Assert.Single(result.Files);
        Assert.Null(result.Note);
        Assert.Contains("aac", encoder.Calls[0]);
    }

    [Fact]
    public async Task M4bCarriesTheChapterMarks()
    {
        await new AudiobookPackager(new FakeEncoder()).PackageAsync(
            Rendered, [Chapter("One"), Chapter("Two")], AudiobookFormat.M4b,
            Path.Combine(Output, "book.m4b"), Book());

        var metadata = await File.ReadAllTextAsync(
            Path.Combine(Rendered, AudiobookPackager.MetadataName));
        Assert.Equal(2, metadata.Split("[CHAPTER]").Length - 1);
        Assert.Contains("title=The Quiet House", metadata, StringComparison.Ordinal);
        Assert.Contains("artist=A Writer", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithNoCover_NoImageIsMappedIn()
    {
        var encoder = new FakeEncoder();

        await new AudiobookPackager(encoder).PackageAsync(
            Rendered, [Chapter("One")], AudiobookFormat.M4b,
            Path.Combine(Output, "book.m4b"), Book());

        Assert.DoesNotContain("attached_pic", encoder.Calls[0]);
    }

    [Fact]
    public async Task WithACover_ItIsAttachedToTheFile()
    {
        var cover = Path.Combine(_folder, "cover.jpg");
        await File.WriteAllBytesAsync(cover, [0xFF, 0xD8]);
        var encoder = new FakeEncoder();

        await new AudiobookPackager(encoder).PackageAsync(
            Rendered, [Chapter("One")], AudiobookFormat.M4b,
            Path.Combine(Output, "book.m4b"), Book(cover));

        Assert.Contains("attached_pic", encoder.Calls[0]);
        Assert.Contains(cover, encoder.Calls[0]);
    }

    [Fact]
    public async Task ACoverThatIsNotThere_IsNotAttached()
    {
        var encoder = new FakeEncoder();

        await new AudiobookPackager(encoder).PackageAsync(
            Rendered, [Chapter("One")], AudiobookFormat.M4b,
            Path.Combine(Output, "book.m4b"), Book(Path.Combine(_folder, "gone.jpg")));

        Assert.DoesNotContain("attached_pic", encoder.Calls[0]);
    }

    [Fact]
    public async Task Mp3IsOneFilePerChapter_Tagged()
    {
        var encoder = new FakeEncoder();

        var result = await new AudiobookPackager(encoder).PackageAsync(
            Rendered, [Chapter("One"), Chapter("Two")], AudiobookFormat.Mp3PerChapter,
            Output, Book());

        Assert.Equal(AudiobookFormat.Mp3PerChapter, result.Format);
        Assert.Equal(2, result.Files.Count);
        Assert.Contains("track=1/2", encoder.Calls[0]);
        Assert.Contains("album=The Quiet House", encoder.Calls[0]);
        Assert.Contains("genre=Audiobook", encoder.Calls[1]);
    }

    [Fact]
    public async Task Mp3FilesAreNumbered_SoAPlayerReadsThemInOrder()
    {
        var result = await new AudiobookPackager(new FakeEncoder()).PackageAsync(
            Rendered, [Chapter("One"), Chapter("Two")], AudiobookFormat.Mp3PerChapter,
            Output, Book());

        Assert.EndsWith("01 - One.mp3", result.Files[0], StringComparison.Ordinal);
        Assert.EndsWith("02 - Two.mp3", result.Files[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithACover_EveryMp3CarriesIt()
    {
        var cover = Path.Combine(_folder, "cover.jpg");
        await File.WriteAllBytesAsync(cover, [0xFF, 0xD8]);
        var encoder = new FakeEncoder();

        await new AudiobookPackager(encoder).PackageAsync(
            Rendered, [Chapter("One")], AudiobookFormat.Mp3PerChapter, Output, Book(cover));

        Assert.Contains("attached_pic", encoder.Calls[0]);
    }

    // ─── without one ────────────────────────────────────────────────

    [Fact]
    public async Task WithNoEncoder_TheRenderingIsStillDelivered()
    {
        // The hours already spent rendering must not be thrown away because a
        // tool is missing.
        var result = await new AudiobookPackager(new FakeEncoder(available: false)).PackageAsync(
            Rendered, [Chapter("One"), Chapter("Two")], AudiobookFormat.M4b,
            Path.Combine(Output, "book.m4b"), Book());

        Assert.Equal(AudiobookFormat.WavPerChapter, result.Format);
        Assert.Equal("no-encoder", result.Note);
        Assert.True(File.Exists(Path.Combine(Output, "01 - One.wav")));
        Assert.True(File.Exists(Path.Combine(Output, "02 - Two.wav")));
    }

    [Fact]
    public async Task WithNoEncoder_TheChaptersAreListedWithTheirStartTimes()
    {
        await new AudiobookPackager(new FakeEncoder(available: false)).PackageAsync(
            Rendered, [Chapter("One", 60_000), Chapter("Two", 90_000)],
            AudiobookFormat.Mp3PerChapter, Output, Book());

        var listing = await File.ReadAllTextAsync(
            Path.Combine(Output, AudiobookPackager.ChaptersName));
        Assert.Contains("0:00:00.000 One", listing, StringComparison.Ordinal);
        Assert.Contains("0:01:00.000 Two", listing, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhenTheEncoderFails_TheRenderingIsStillDelivered()
    {
        var result = await new AudiobookPackager(new FakeEncoder(exitCode: 1)).PackageAsync(
            Rendered, [Chapter("One")], AudiobookFormat.M4b,
            Path.Combine(Output, "book.m4b"), Book());

        Assert.Equal(AudiobookFormat.WavPerChapter, result.Format);
        Assert.Equal("encoder-failed", result.Note);
        Assert.True(File.Exists(Path.Combine(Output, "01 - One.wav")));
    }

    [Fact]
    public async Task WhenAnMp3Fails_TheWholeDeliveryFallsBackRatherThanBeingHalfDone()
    {
        var result = await new AudiobookPackager(new FakeEncoder(exitCode: 1)).PackageAsync(
            Rendered, [Chapter("One"), Chapter("Two")], AudiobookFormat.Mp3PerChapter,
            Output, Book());

        Assert.Equal(AudiobookFormat.WavPerChapter, result.Format);
        Assert.Equal("encoder-failed", result.Note);
    }

    [Fact]
    public async Task AskingForWav_NeedsNoEncoderAtAll()
    {
        var result = await new AudiobookPackager(new FakeEncoder(available: false)).PackageAsync(
            Rendered, [Chapter("One")], AudiobookFormat.WavPerChapter, Output, Book());

        Assert.Equal(AudiobookFormat.WavPerChapter, result.Format);
        Assert.Null(result.Note);
    }

    [Fact]
    public async Task DeliveringIntoTheFolderItCameFrom_DoesNotCopyAFileOverItself()
    {
        var chapter = Chapter("One");

        var result = await new AudiobookPackager(new FakeEncoder()).PackageAsync(
            Rendered, [chapter], AudiobookFormat.WavPerChapter, Rendered, Book());

        Assert.Equal(AudiobookFormat.WavPerChapter, result.Format);
        Assert.True(File.Exists(Path.Combine(Rendered, chapter.File)));
    }

    // ─── nothing to package ─────────────────────────────────────────

    [Fact]
    public async Task WithNothingRendered_ItSaysSoRatherThanWritingAnEmptyBook()
    {
        var result = await new AudiobookPackager(new FakeEncoder()).PackageAsync(
            Rendered, [], AudiobookFormat.M4b, Path.Combine(Output, "book.m4b"), Book());

        Assert.Equal("nothing-rendered", result.Note);
        Assert.Empty(result.Files);
    }

    [Fact]
    public async Task AChapterWhoseFileVanished_IsLeftOutOfTheBook()
    {
        var missing = new NarrationChapterAudio("x", "Gone", "not-there.wav", 1000, 0, false);

        var result = await new AudiobookPackager(new FakeEncoder()).PackageAsync(
            Rendered, [Chapter("One"), missing], AudiobookFormat.Mp3PerChapter, Output, Book());

        Assert.Single(result.Files);
    }

    // ─── the details ────────────────────────────────────────────────

    [Fact]
    public void ChapterMarksDoNotOverlap()
    {
        var metadata = AudiobookPackager.Metadata(
            [
                new NarrationChapterAudio("a", "One", "a.wav", 1000, 0, false),
                new NarrationChapterAudio("b", "Two", "b.wav", 1000, 0, false)
            ],
            Book());

        // The second chapter starts where the first ends - and the first ends a
        // millisecond earlier, so skipping forward lands on the first word of
        // chapter two rather than the last of chapter one.
        Assert.Contains("START=0\r\nEND=999", metadata.ReplaceLineEndings("\r\n"), StringComparison.Ordinal);
        Assert.Contains("START=1000\r\nEND=1999", metadata.ReplaceLineEndings("\r\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void ATitleWithASemicolonInIt_DoesNotTruncateItself()
    {
        // ffmetadata takes ; = # and \ as structure.
        var metadata = AudiobookPackager.Metadata(
            [], new AudiobookMetadata { Title = "Notes; or, the Long Way #2 = home" });

        Assert.Contains(@"title=Notes\; or, the Long Way \#2 \= home", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void ATitleWithALineBreakInIt_StaysOnOneLine()
    {
        var metadata = AudiobookPackager.Metadata(
            [], new AudiobookMetadata { Title = "One\nTwo" });

        Assert.Contains("title=One Two", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyTagsAreLeftOutRatherThanWrittenBlank()
    {
        var metadata = AudiobookPackager.Metadata([], new AudiobookMetadata());

        Assert.DoesNotContain("artist=", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void APlayListQuotesTheNamesItLists()
    {
        var list = AudiobookPackager.ConcatList(
            [new NarrationChapterAudio("a", "One", "it's here.wav", 0, 0, false)]);

        Assert.Equal("file 'it'\\''s here.wav'", list.Trim());
    }

    [Theory]
    [InlineData("Chapter One", "Chapter One")]
    [InlineData("Book 1/2", "Book 1 2")]
    [InlineData("   ", "chapter")]
    [InlineData("", "chapter")]
    public void AChapterTitleBecomesAFileNameWithoutBecomingAPath(string title, string expected)
        => Assert.Equal(expected, AudiobookPackager.SafeName(title));

    [Fact]
    public void AFallbackForM4b_GoesBesideTheFileItWouldHaveWritten()
        => Assert.Equal(
            Path.Combine("C:", "books"),
            AudiobookPackager.FallbackFolder(
                AudiobookFormat.M4b, Path.Combine("C:", "books", "book.m4b"), "render"));

    [Fact]
    public void AFallbackForM4bWithNoFolderInThePath_LandsWhereTheRenderIs()
        => Assert.Equal(
            "render", AudiobookPackager.FallbackFolder(AudiobookFormat.M4b, "book.m4b", "render"));

    [Fact]
    public void AFallbackForTheOthers_IsTheFolderTheyWereGiven()
        => Assert.Equal(
            "out",
            AudiobookPackager.FallbackFolder(AudiobookFormat.Mp3PerChapter, "out", "render"));

    [Fact]
    public void APackagerWithNoEncoderNamed_UsesTheOneOnThePath()
    {
        // Constructed rather than run: the assertion is that the default exists,
        // not that this machine has ffmpeg.
        Assert.NotNull(new AudiobookPackager());
    }
}
