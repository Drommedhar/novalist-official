using System.Security.Cryptography;

namespace Novalist.Core.Services;

/// <summary>
/// Where rendered speech is put so the interface can play it.
///
/// Beside the application, never in the project. A reading of a whole book is
/// tens of megabytes of audio that can be made again from the manuscript at any
/// time, and a repository should not grow by that much because somebody pressed
/// Play. The designed voices are the opposite case and do live in the project:
/// those cannot be made again.
///
/// Base64 over the wire was the alternative and is the wrong transport for
/// audio - it inflates every clip by a third and puts the whole reading through
/// a JSON parser. The interface is handed a name and fetches the bytes itself.
/// </summary>
public sealed class NarrationClipCache
{
    /// <summary>The folder's name under the application data directory. Shared
    /// with the interface, which serves it, so it is a constant rather than a
    /// string written twice.</summary>
    public const string FolderName = "narration-cache";

    private readonly string _root;

    /// <param name="settingsDirectory">Where Novalist keeps its own files;
    /// defaults to the application-data folder, as the other services do.</param>
    public NarrationClipCache(string? settingsDirectory = null)
    {
        var folder = settingsDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Novalist");
        _root = Path.Combine(folder, FolderName);
    }

    /// <summary>The folder itself, for the interface to serve from.</summary>
    public string Root => _root;

    /// <summary>
    /// Writes one clip and returns the name to fetch it by.
    ///
    /// Named after the audio's own content, so the same line rendered twice is
    /// written once and a name can never collide with another reading's.
    /// </summary>
    public async Task<string> WriteAsync(byte[] audio, string format)
    {
        Directory.CreateDirectory(_root);
        var name = Name(audio, format);
        var path = Path.Combine(_root, name);
        if (!File.Exists(path))
            await File.WriteAllBytesAsync(path, audio);
        return name;
    }

    /// <summary>
    /// Empties the cache. Called when a reading stops and when the project
    /// closes: audio of somebody's manuscript should not outlive the sitting it
    /// was made in.
    /// </summary>
    public void Clear()
    {
        if (!Directory.Exists(_root))
            return;

        foreach (var file in Directory.EnumerateFiles(_root))
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Being played right now. It will go with the next clear rather
                // than taking the stop with it.
            }
        }
    }

    /// <summary>How much audio is sitting in the cache, in bytes. For the
    /// diagnostics log, which may know sizes and never content.</summary>
    public long Size()
    {
        var directory = new DirectoryInfo(_root);
        if (!directory.Exists)
            return 0;

        // Lengths come from the directory scan itself rather than from a second
        // look at each file, so a clip deleted while this runs is counted from
        // what the scan saw instead of throwing part way through.
        return directory.EnumerateFiles().Sum(file => file.Length);
    }

    /// <summary>
    /// A clip's name: the hash of its bytes, plus a container extension that is
    /// only ever letters and digits. Nothing the writer typed reaches a file
    /// name, so nothing about their story can be read off the cache folder.
    /// </summary>
    private static string Name(byte[] audio, string format)
    {
        var hash = Convert.ToHexString(SHA256.HashData(audio), 0, 12).ToLowerInvariant();
        var trimmed = (format ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
        var extension = trimmed.Length > 0 && trimmed.All(char.IsLetterOrDigit) ? trimmed : "wav";
        return $"{hash}.{extension}";
    }

    /// <summary>The bytes of one cached clip, or null when it is not there. The
    /// name is checked rather than trusted: a caller that passed a path would
    /// otherwise reach outside the cache.</summary>
    public async Task<byte[]?> ReadAsync(string name)
    {
        if (!IsCleanName(name))
            return null;

        var path = Path.Combine(_root, name);
        if (!File.Exists(path))
            return null;

        try
        {
            return await File.ReadAllBytesAsync(path);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>A name this cache could have written: hex, a dot, and an
    /// extension. Anything else is somebody else's idea.</summary>
    /// <summary>
    /// Several clips at once, by name, skipping any that are no longer there.
    ///
    /// For emotion-reference clips: the writer points at a line they liked the
    /// sound of, and the clip that was is fetched back out of the cache. One
    /// that has been cleared since is simply absent, and the line is performed
    /// on its vector instead - a render that refused to start because one
    /// reference had expired would be worse than one that reads it plainly.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, byte[]>> ReadManyAsync(
        IReadOnlyCollection<string> names)
    {
        var found = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (await ReadAsync(name) is { } audio)
                found[name] = audio;
        }
        return found;
    }

    internal static bool IsCleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var dot = name.IndexOf('.');
        if (dot <= 0 || dot == name.Length - 1)
            return false;

        var stem = name[..dot];
        var extension = name[(dot + 1)..];
        return stem.All(Uri.IsHexDigit)
            && extension.Length > 0
            && extension.All(char.IsLetterOrDigit);
    }

    /// <summary>The name a clip would be written under, without writing it.
    /// Lets a caller say whether a render is already cached.</summary>
    public static string NameFor(byte[] audio, string format) => Name(audio, format);
}
