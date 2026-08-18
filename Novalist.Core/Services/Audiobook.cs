using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;


namespace Novalist.Core.Services;

/// <summary>What the writer asked for.</summary>
public enum AudiobookFormat
{
    /// <summary>One file, with chapter marks, cover and metadata. What an
    /// audiobook is, and what a player expects.</summary>
    M4b,

    /// <summary>One MP3 per chapter, tagged. The plain alternative: it plays
    /// everywhere, including in cars and on players that never learned M4B.</summary>
    Mp3PerChapter,

    /// <summary>The rendered audio, untouched. No encoder needed, so this is
    /// always available - and it is what the other two fall back to.</summary>
    WavPerChapter
}

/// <summary>The book's details, as they are written into the file's tags.</summary>
public sealed class AudiobookMetadata
{
    public string Title { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string Year { get; init; } = string.Empty;

    /// <summary>Absolute path of the cover image, when the book has one.</summary>
    public string CoverPath { get; init; } = string.Empty;
}

/// <summary>What packaging produced.</summary>
/// <param name="Format">What was actually written, which is not always what was
/// asked for - see <paramref name="Note"/>.</param>
/// <param name="Files">Absolute paths, in reading order.</param>
/// <param name="Note">Why the format changed, when it did. Null on success.</param>
public sealed record AudiobookResult(
    AudiobookFormat Format, IReadOnlyList<string> Files, string? Note);

/// <summary>
/// Whatever runs the encoder.
///
/// A seam rather than a call, because everything worth testing about packaging
/// is which arguments get built - the chapter marks, the tags, the cover, the
/// order - and none of that needs a real encoder on the machine to check.
/// </summary>
public interface IMediaEncoder
{
    /// <summary>Whether an encoder is on this machine at all.</summary>
    Task<bool> AvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs it. Returns the exit code and whatever it said.</summary>
    Task<(int ExitCode, string Output)> RunAsync(
        IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);
}

/// <summary>
/// ffmpeg, if it is installed.
///
/// Novalist does not ship it and does not fetch it. It is several hundred
/// megabytes under a licence that would change what Novalist itself is
/// distributed as, and the feature works without it - <see
/// cref="AudiobookFormat.WavPerChapter"/> needs no encoder at all. What ffmpeg
/// adds is the packaging: one file, chapter marks, a cover, and a size a person
/// can actually send to somebody.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Launches a real process; the decisions are in AudiobookPackager.")]
public sealed class FfmpegEncoder : IMediaEncoder
{
    private readonly string _executable;

    public FfmpegEncoder(string? executable = null)
    {
        _executable = string.IsNullOrWhiteSpace(executable) ? "ffmpeg" : executable;
    }

    public async Task<bool> AvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var (code, _) = await RunAsync(["-version"], cancellationToken);
            return code == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    public async Task<(int ExitCode, string Output)> RunAsync(
        IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        var start = new ProcessStartInfo(_executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("ffmpeg did not start");
        var output = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, output);
    }
}

/// <summary>
/// Turns rendered chapters into something a person can play.
///
/// The render job writes WAV, because WAV is what can be assembled exactly and
/// checked. Nobody wants a nine-hour WAV: this is the step that makes it an
/// audiobook - chapter marks a player can skip through, the cover, the author,
/// and a bitrate that fits on a phone.
///
/// When there is no encoder the WAVs are still delivered, with a chapter list
/// beside them, and the result says why. A feature that refuses to finish
/// because a tool is missing has thrown away the hours of rendering that led up
/// to it.
/// </summary>
public sealed class AudiobookPackager
{
    /// <summary>Names the chapter list written beside a WAV delivery.</summary>
    public const string ChaptersName = "chapters.txt";

    /// <summary>ffmpeg's own metadata format, with chapters in milliseconds.</summary>
    internal const string MetadataName = "audiobook.ffmetadata";

    /// <summary>The concat demuxer's play list.</summary>
    internal const string ListName = "audiobook.concat";

    /// <summary>Spoken word, mono, at a size that is not silly for nine hours.</summary>
    private const string SpeechBitrate = "64k";

    private readonly IMediaEncoder _encoder;

    public AudiobookPackager(IMediaEncoder? encoder = null)
    {
        _encoder = encoder ?? new FfmpegEncoder();
    }

    /// <summary>
    /// Packages rendered chapters.
    /// </summary>
    /// <param name="folder">Where the rendered WAVs are.</param>
    /// <param name="chapters">The chapters, in reading order.</param>
    /// <param name="format">What the writer asked for.</param>
    /// <param name="outputPath">The file for <see cref="AudiobookFormat.M4b"/>;
    /// the folder to fill for the per-chapter formats.</param>
    public async Task<AudiobookResult> PackageAsync(
        string folder,
        IReadOnlyList<NarrationChapterAudio> chapters,
        AudiobookFormat format,
        string outputPath,
        AudiobookMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var present = chapters
            .Where(c => File.Exists(Path.Combine(folder, c.File)))
            .ToArray();
        if (present.Length == 0)
            return new AudiobookResult(AudiobookFormat.WavPerChapter, [], "nothing-rendered");

        if (format != AudiobookFormat.WavPerChapter
            && !await _encoder.AvailableAsync(cancellationToken))
        {
            // The rendering is the expensive part and it is already done. Hand
            // it over as it stands and say what is missing, rather than losing it.
            var copied = await CopyAsync(
                folder, present, FallbackFolder(format, outputPath, folder), cancellationToken);
            return new AudiobookResult(AudiobookFormat.WavPerChapter, copied, "no-encoder");
        }

        return format switch
        {
            AudiobookFormat.M4b => await BuildM4bAsync(
                folder, present, outputPath, metadata, cancellationToken),
            AudiobookFormat.Mp3PerChapter => await BuildMp3Async(
                folder, present, outputPath, metadata, cancellationToken),
            _ => new AudiobookResult(
                AudiobookFormat.WavPerChapter,
                await CopyAsync(folder, present, outputPath, cancellationToken),
                null)
        };
    }

    private async Task<AudiobookResult> BuildM4bAsync(
        string folder,
        IReadOnlyList<NarrationChapterAudio> chapters,
        string outputPath,
        AudiobookMetadata metadata,
        CancellationToken cancellationToken)
    {
        var listPath = Path.Combine(folder, ListName);
        var metaPath = Path.Combine(folder, MetadataName);
        await File.WriteAllTextAsync(listPath, ConcatList(chapters), cancellationToken);
        await File.WriteAllTextAsync(
            metaPath, Metadata(chapters, metadata), new UTF8Encoding(false), cancellationToken);

        var cover = HasCover(metadata) ? metadata.CoverPath : null;
        var arguments = new List<string>
        {
            "-y", "-f", "concat", "-safe", "0", "-i", listPath, "-i", metaPath
        };
        if (cover != null)
            arguments.AddRange(["-i", cover]);
        arguments.AddRange(["-map", "0:a", "-map_metadata", "1"]);
        if (cover != null)
        {
            arguments.AddRange([
                "-map", "2:v", "-c:v", "copy", "-disposition:v:0", "attached_pic"
            ]);
        }
        arguments.AddRange([
            "-c:a", "aac", "-b:a", SpeechBitrate, "-movflags", "+faststart", outputPath
        ]);

        var (code, _) = await _encoder.RunAsync(arguments, cancellationToken);
        if (code != 0)
        {
            var copied = await CopyAsync(
                folder, chapters, FallbackFolder(AudiobookFormat.M4b, outputPath, folder),
                cancellationToken);
            return new AudiobookResult(AudiobookFormat.WavPerChapter, copied, "encoder-failed");
        }

        return new AudiobookResult(AudiobookFormat.M4b, [outputPath], null);
    }

    private async Task<AudiobookResult> BuildMp3Async(
        string folder,
        IReadOnlyList<NarrationChapterAudio> chapters,
        string outputFolder,
        AudiobookMetadata metadata,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputFolder);
        var cover = HasCover(metadata) ? metadata.CoverPath : null;
        var written = new List<string>(chapters.Count);

        for (var index = 0; index < chapters.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chapter = chapters[index];
            var target = Path.Combine(
                outputFolder, $"{index + 1:D2} - {SafeName(chapter.Title)}.mp3");

            var arguments = new List<string> { "-y", "-i", Path.Combine(folder, chapter.File) };
            if (cover != null)
            {
                arguments.AddRange([
                    "-i", cover,
                    "-map", "0:a", "-map", "1:v", "-c:v", "copy",
                    "-id3v2_version", "3", "-disposition:v", "attached_pic"
                ]);
            }
            arguments.AddRange([
                "-c:a", "libmp3lame", "-b:a", "96k",
                "-metadata", $"title={chapter.Title}",
                "-metadata", $"album={metadata.Title}",
                "-metadata", $"artist={metadata.Author}",
                "-metadata", $"track={index + 1}/{chapters.Count}",
                "-metadata", "genre=Audiobook",
                target
            ]);

            var (code, _) = await _encoder.RunAsync(arguments, cancellationToken);
            if (code != 0)
            {
                var copied = await CopyAsync(folder, chapters, outputFolder, cancellationToken);
                return new AudiobookResult(
                    AudiobookFormat.WavPerChapter, copied, "encoder-failed");
            }
            written.Add(target);
        }

        return new AudiobookResult(AudiobookFormat.Mp3PerChapter, written, null);
    }

    /// <summary>
    /// Delivers the rendered audio as it is, with a chapter list beside it.
    ///
    /// The list is not decoration. Without it, a folder of files named after
    /// their position is the only record of what order the book goes in, and
    /// the writer has to open each one to find out.
    /// </summary>
    private static async Task<IReadOnlyList<string>> CopyAsync(
        string folder,
        IReadOnlyList<NarrationChapterAudio> chapters,
        string outputFolder,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputFolder);
        var written = new List<string>(chapters.Count + 1);
        var listing = new StringBuilder();
        var at = 0d;

        for (var index = 0; index < chapters.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chapter = chapters[index];
            var target = Path.Combine(
                outputFolder, $"{index + 1:D2} - {SafeName(chapter.Title)}.wav");
            var source = Path.Combine(folder, chapter.File);
            if (!string.Equals(
                    Path.GetFullPath(source), Path.GetFullPath(target),
                    StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(source, target, overwrite: true);
            }
            written.Add(target);

            listing.Append(Timestamp(at)).Append(' ').AppendLine(chapter.Title);
            at += chapter.DurationMs;
        }

        var listPath = Path.Combine(outputFolder, ChaptersName);
        await File.WriteAllTextAsync(listPath, listing.ToString(), cancellationToken);
        written.Add(listPath);
        return written;
    }

    /// <summary>
    /// The concat demuxer's play list.
    ///
    /// Single quotes are the escape the demuxer defines, and a chapter file is
    /// named by this application rather than by the writer - but the quoting is
    /// done properly anyway, because "our own names are safe" is exactly the
    /// assumption that stops being true later.
    /// </summary>
    internal static string ConcatList(IReadOnlyList<NarrationChapterAudio> chapters)
    {
        var builder = new StringBuilder();
        foreach (var chapter in chapters)
            builder.Append("file '").Append(chapter.File.Replace("'", @"'\''")).AppendLine("'");
        return builder.ToString();
    }

    /// <summary>
    /// The book's tags and its chapter marks, in ffmpeg's metadata format.
    ///
    /// Chapter ends are exclusive of the next chapter's start, so a player
    /// skipping forward lands on the first word rather than the last of the
    /// chapter before.
    /// </summary>
    internal static string Metadata(
        IReadOnlyList<NarrationChapterAudio> chapters, AudiobookMetadata metadata)
    {
        var builder = new StringBuilder();
        builder.AppendLine(";FFMETADATA1");
        Tag(builder, "title", metadata.Title);
        Tag(builder, "artist", metadata.Author);
        Tag(builder, "album", metadata.Title);
        Tag(builder, "album_artist", metadata.Author);
        Tag(builder, "genre", "Audiobook");
        Tag(builder, "language", metadata.Language);
        Tag(builder, "date", metadata.Year);
        Tag(builder, "description", metadata.Description);

        var at = 0L;
        foreach (var chapter in chapters)
        {
            var end = at + (long)Math.Round(chapter.DurationMs);
            builder.AppendLine("[CHAPTER]");
            builder.AppendLine("TIMEBASE=1/1000");
            builder.Append("START=").AppendLine(at.ToString(CultureInfo.InvariantCulture));
            builder.Append("END=").AppendLine(
                Math.Max(at, end - 1).ToString(CultureInfo.InvariantCulture));
            Tag(builder, "title", chapter.Title);
            at = end;
        }

        return builder.ToString();
    }

    /// <summary>
    /// One metadata line, with the characters that would end it escaped.
    ///
    /// ffmetadata takes <c>=</c>, <c>;</c>, <c>#</c>, <c>\</c> and a newline as
    /// structure. A book called "Notes; or, the Long Way Round" would otherwise
    /// truncate its own title.
    /// </summary>
    private static void Tag(StringBuilder builder, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        builder.Append(key).Append('=');
        foreach (var character in value)
        {
            if (character is '=' or ';' or '#' or '\\')
                builder.Append('\\');
            builder.Append(character is '\n' or '\r' ? ' ' : character);
        }
        builder.AppendLine();
    }

    /// <summary>A chapter title as a file name, with the separators taken out.</summary>
    internal static string SafeName(string title)
    {
        var cleaned = new string([.. (title ?? string.Empty)
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? ' ' : c)])
            .Trim();
        return cleaned.Length == 0 ? "chapter" : cleaned;
    }

    /// <summary>h:mm:ss.mmm, which is what a chapter list is read in.</summary>
    internal static string Timestamp(double milliseconds)
        => TimeSpan.FromMilliseconds(milliseconds).ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture);

    /// <summary>
    /// Where the WAVs go when the packaging cannot happen.
    ///
    /// The per-chapter formats were already given a folder. M4B was given a
    /// file name, and writing a folder of chapters into a path that names a
    /// file is how a delivery ends up somewhere nobody looks - so it becomes
    /// the folder that file was going to live in.
    /// </summary>
    internal static string FallbackFolder(
        AudiobookFormat asked, string outputPath, string renderFolder)
        => asked == AudiobookFormat.M4b
            ? (Path.GetDirectoryName(outputPath) is { Length: > 0 } parent ? parent : renderFolder)
            : outputPath;

    private static bool HasCover(AudiobookMetadata metadata)
        => !string.IsNullOrWhiteSpace(metadata.CoverPath) && File.Exists(metadata.CoverPath);
}
