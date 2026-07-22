using Novalist.Core.Models;

namespace Novalist.Core.Utilities;

/// <summary>
/// Resolves a scene's effective in-world date, mirroring the Avalonia
/// <c>ContextSidebarViewModel.ResolveContextDateDisplay</c> precedence:
/// scene range, then scene date, then chapter range, then chapter date.
/// Shared by the Context sidebar (via <c>scenes/getMeta</c>) and the Wiki
/// view's appearances timeline so both agree on the displayed date.
/// </summary>
public static class SceneStoryDate
{
    /// <summary>The formatted display date, or an empty string when neither the
    /// scene nor its chapter carries any date.</summary>
    public static string Resolve(ChapterData chapter, SceneData scene)
    {
        if (scene.DateRange?.HasValue == true)
            return StoryDateFormatter.FormatRange(scene.DateRange);
        if (!string.IsNullOrWhiteSpace(scene.Date))
            return scene.Date.Trim();
        if (chapter.DateRange?.HasValue == true)
            return StoryDateFormatter.FormatRange(chapter.DateRange);
        return chapter.Date.Trim();
    }
}
