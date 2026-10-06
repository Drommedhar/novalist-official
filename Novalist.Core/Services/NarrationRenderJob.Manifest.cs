using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Novalist.Core.Utilities;
using Novalist.Sdk.Models.Narration;

namespace Novalist.Core.Services;

public sealed partial class NarrationRenderJob
{
    /// <summary>Which chapters are on disk already, by guid, so an estimate can
    /// say how much of the work is actually left.</summary>
    public IReadOnlyDictionary<string, double> Rendered() => RenderedIn(_folder);

    /// <summary>
    /// The same, for a caller that only wants to look.
    ///
    /// Static because reading the manifest needs no engine, and a caller forced
    /// to construct a job just to ask would have to invent one.
    /// </summary>
    public static IReadOnlyDictionary<string, double> RenderedIn(string folder)
        => ReadManifest(folder)
            .ToDictionary(p => p.Key, p => p.Value.DurationMs, StringComparer.Ordinal);

    /// <summary>Forgets every rendered chapter, so the next run does all of
    /// them. What "render again from scratch" means.</summary>
    public void Reset()
    {
        if (!Directory.Exists(_folder))
            return;
        foreach (var file in Directory.EnumerateFiles(_folder))
        {
            try
            {
                NarrationFileCleanup.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private Dictionary<string, ManifestEntry> ReadManifest() => ReadManifest(_folder);

    private static Dictionary<string, ManifestEntry> ReadManifest(string folder)
    {
        var path = Path.Combine(folder, ManifestName);
        if (!File.Exists(path))
            return new Dictionary<string, ManifestEntry>(StringComparer.Ordinal);
        try
        {
            var read = JsonSerializer.Deserialize<Dictionary<string, ManifestEntry>>(
                File.ReadAllText(path));
            return read == null
                ? new Dictionary<string, ManifestEntry>(StringComparer.Ordinal)
                : new Dictionary<string, ManifestEntry>(read, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A manifest that cannot be read means rendering again, which is
            // slow but correct. Refusing to render at all would not be.
            return new Dictionary<string, ManifestEntry>(StringComparer.Ordinal);
        }
    }

    private void WriteManifest(Dictionary<string, ManifestEntry> manifest)
    {
        try
        {
            File.WriteAllText(
                Path.Combine(_folder, ManifestName), JsonSerializer.Serialize(manifest, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>One chapter's line in the manifest.</summary>
    internal sealed record ManifestEntry(string Stamp, string File, double DurationMs, int Missing);
}
