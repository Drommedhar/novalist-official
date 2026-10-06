using System.Diagnostics;
using Novalist.Backend.Extensions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class ManuscriptImportRpc
{
    /// <summary>
    /// Adds a scene-scoped manuscript property for each of the project's custom
    /// metadata fields, and returns the Scrivener field id to Novalist key map
    /// the scenes are written through.
    ///
    /// Custom metadata is as close as Scrivener gets to a field of the writer's
    /// own - a tension rating, a POV name, a draft flag. Dropping it forced
    /// whoever relied on it back onto the synopsis card.
    /// </summary>
    private static Dictionary<string, string> DeclareCustomFields(
        BookData book, ScrivenerProject project)
    {
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var field in project.CustomFields)
        {
            var existing = book.ManuscriptProperties.FirstOrDefault(p =>
                p.Scope == ManuscriptPropertyScope.Scene
                && string.Equals(p.Label, field.Title, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                keys[field.Id] = existing.Key;
                continue;
            }

            var definition = new ManuscriptPropertyDefinition
            {
                Key = KeyFrom(field.Title),
                Label = field.Title,
                Scope = ManuscriptPropertyScope.Scene,
                // A Scrivener list field is a closed vocabulary, which is
                // exactly what an enum property is; everything else is text.
                Type = field.Options.Count > 0 ? CustomPropertyType.Enum : CustomPropertyType.String,
                EnumOptions = field.Options.Count > 0 ? [.. field.Options] : null
            };
            book.ManuscriptProperties.Add(definition);
            keys[field.Id] = definition.Key;
        }

        return keys;
    }

    /// <summary>
    /// The book's stage for a Scrivener status, adding it when the book has no
    /// stage by that name. A writer whose statuses are "Zero draft" and "Beta"
    /// gets those, not the nearest of Novalist's five.
    /// </summary>
    private static string StageKeyFor(BookData book, string status)
    {
        var existing = book.SceneStages
            .FirstOrDefault(s => string.Equals(s.Label, status, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing.Key;

        var stage = new SceneStage { Key = KeyFrom(status), Label = status };
        book.SceneStages.Add(stage);
        return stage.Key;
    }

    /// <summary>The book's label for a Scrivener label, adding it when absent.</summary>
    private static string LabelKeyFor(BookData book, string label)
    {
        var existing = book.SceneLabels
            .FirstOrDefault(l => string.Equals(l.Label, label, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing.Key;

        var created = new SceneLabel { Key = KeyFrom(label), Label = label };
        book.SceneLabels.Add(created);
        return created.Key;
    }

    private static string KeyFrom(string name)
    {
        var key = new string([.. name.ToLowerInvariant().Where(char.IsLetterOrDigit)]);
        return key.Length > 0 ? key : Guid.NewGuid().ToString("N")[..8];
    }

    private static ImportPlanDto ToDto(ImportPlan plan) =>
        new(
            plan.Format,
            plan.Chapters.Count,
            plan.SceneCount,
            plan.WordCount,
            plan.Chapters
                .Select(c => new ImportChapterDto(
                    c.Title,
                    string.Empty,
                    c.Scenes.Select(s => new ImportSceneDto(s.Title, s.WordCount)).ToArray()))
                .ToArray(),
            [], 0, 0, 0, 0, [], []);
}
