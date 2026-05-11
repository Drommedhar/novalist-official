using System.Collections.Generic;
using System.Linq;
using Novalist.Core.Models;

namespace Novalist.Core.Utilities;

/// <summary>
/// Resolves the effective in-world date range for a scene using the fallback
/// chain Scene → Chapter → Act. Falls back to legacy <see cref="SceneData.Date"/>
/// / <see cref="ChapterData.Date"/> strings when no <see cref="StoryDateRange"/>
/// is present.
/// </summary>
public static class StoryDateResolver
{
    public static StoryDateRange? Resolve(SceneData? scene, ChapterData? chapter, IReadOnlyList<ActData>? acts)
    {
        if (scene?.DateRange?.HasValue == true) return scene.DateRange;
        if (!string.IsNullOrWhiteSpace(scene?.Date)) return new StoryDateRange { Start = scene!.Date };

        if (chapter?.DateRange?.HasValue == true) return chapter.DateRange;
        if (!string.IsNullOrWhiteSpace(chapter?.Date)) return new StoryDateRange { Start = chapter!.Date };

        if (chapter != null && acts != null && !string.IsNullOrWhiteSpace(chapter.Act))
        {
            var act = acts.FirstOrDefault(a =>
                string.Equals(a.Name, chapter.Act, System.StringComparison.OrdinalIgnoreCase));
            if (act?.DateRange?.HasValue == true) return act.DateRange;
        }

        return null;
    }
}
