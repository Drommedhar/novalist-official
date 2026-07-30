using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// A file kept with a Codex entry: a recording, a scan, a PDF, a link.
///
/// Entries could hold images and nothing else. A recorded interview with the
/// person a character is based on, the deed that settles who owns the house, a
/// pronunciation clip for a name nobody can say - all of it had to live as a
/// Research item and be linked back, stored and surfaced somewhere other than
/// the entry it belongs to.
/// </summary>
public sealed class EntityAttachment
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>What to call it. Falls back to the file name when left blank.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Book-relative path of the copied file, or empty for a link.
    ///
    /// Copied into the project rather than referenced in place: a path into
    /// somebody's Downloads folder is a file that will be gone by the time
    /// anyone follows it, and the project has to stay portable.
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// A web address, for an attachment that is a link rather than a file.
    /// Empty on a real file.
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>What the entry is, so a surface can decide how to show it.</summary>
    [JsonPropertyName("kind")]
    public AttachmentKind Kind { get; set; } = AttachmentKind.File;

    /// <summary>Why it is here, in the writer's words.</summary>
    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;

    /// <summary>True when this is a link rather than a copied file.</summary>
    [JsonIgnore]
    public bool IsLink => Kind == AttachmentKind.Link;
}

/// <summary>
/// What an attachment is.
///
/// Decided from the file extension rather than sniffed: the writer sees the
/// difference between a recording and a document immediately, and a wrong guess
/// on a rare format costs nothing but an icon.
/// </summary>
public enum AttachmentKind
{
    /// <summary>Anything with no better description.</summary>
    File = 0,
    Audio = 1,
    Video = 2,
    Document = 3,

    /// <summary>A web address. Nothing was copied into the project.</summary>
    Link = 4
}

/// <summary>Deciding what an attached file is, from its name.</summary>
public static class AttachmentKinds
{
    private static readonly HashSet<string> Audio =
        new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".m4a", ".wav", ".ogg", ".flac", ".aac", ".opus" };

    private static readonly HashSet<string> Video =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".webm", ".mov", ".mkv", ".avi", ".m4v" };

    private static readonly HashSet<string> Document =
        new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".docx", ".doc", ".odt", ".rtf", ".txt", ".md", ".epub" };

    /// <summary>What a file with this name is.</summary>
    public static AttachmentKind Of(string? fileName)
    {
        var extension = System.IO.Path.GetExtension(fileName ?? string.Empty);
        if (Audio.Contains(extension)) return AttachmentKind.Audio;
        if (Video.Contains(extension)) return AttachmentKind.Video;
        if (Document.Contains(extension)) return AttachmentKind.Document;
        // Everything else is a file. An unknown format still attaches and still
        // opens; only the icon is less specific.
        return AttachmentKind.File;
    }
}
