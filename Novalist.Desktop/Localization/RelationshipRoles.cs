using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Novalist.Desktop.Localization;

/// <summary>
/// Aggregates relationship role keywords (father, mother, sibling, etc.) from
/// every locale file under <see cref="Loc.LocalesDirectory"/>. The graph layout
/// uses these keywords to classify roles independently of the active UI
/// language, so the union of all locales is always loaded. New languages can
/// be supported by dropping a JSON file with a "relationships" section.
///
/// Expected JSON shape (per locale file):
/// <code>
/// "relationships": {
///   "parent":  [ "father", "mother", ... ],
///   "child":   [ "son", "daughter", ... ],
///   "partner": [ "spouse", ... ],
///   "sibling": [ "brother", "sister", ... ],
///   "pseudo":  [ "uncle", "cousin", ... ]
/// }
/// </code>
/// </summary>
public static class RelationshipRoles
{
    private static readonly object _lock = new();
    private static IReadOnlyDictionary<string, IReadOnlyList<string>>? _cache;

    public static IReadOnlyList<string> Parent => Get("parent");
    public static IReadOnlyList<string> Child => Get("child");
    public static IReadOnlyList<string> Partner => Get("partner");
    public static IReadOnlyList<string> Sibling => Get("sibling");
    public static IReadOnlyList<string> Pseudo => Get("pseudo");

    public static IReadOnlyList<string> Get(string roleType)
    {
        EnsureLoaded();
        return _cache!.TryGetValue(roleType, out var list) ? list : Array.Empty<string>();
    }

    /// <summary>Drop the cache so the next access rescans the locales directory.</summary>
    public static void Reload()
    {
        lock (_lock) _cache = null;
    }

    private static void EnsureLoaded()
    {
        if (_cache != null) return;
        lock (_lock)
        {
            _cache ??= LoadFromAllLocales();
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> LoadFromAllLocales()
    {
        var dir = Loc.Instance.LocalesDirectory;
        var merged = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });

                if (!doc.RootElement.TryGetProperty("relationships", out var rels) ||
                    rels.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var prop in rels.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.Array) continue;

                    if (!merged.TryGetValue(prop.Name, out var set))
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        merged[prop.Name] = set;
                    }

                    foreach (var item in prop.Value.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String) continue;
                        var s = item.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) set.Add(s);
                    }
                }
            }
            catch
            {
                // Malformed locale file — skip silently. The remaining files still contribute.
            }
        }

        return merged.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<string>)kvp.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }
}
