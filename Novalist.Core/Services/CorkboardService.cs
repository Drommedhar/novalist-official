using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>One card's place on the freeform board.</summary>
public sealed record CardPlacement(string SceneId, int X, int Y);

/// <summary>
/// Where the writer put each card on the freeform corkboard.
///
/// The corkboard could only ever lay cards out in reading order, grouped by
/// chapter, which is the one arrangement the binder already shows. Planning on
/// index cards is about the arrangements it could not: three piles for three
/// threads, a row of scenes that all happen the same night, an outlier pushed
/// to the side because it does not fit yet.
/// </summary>
public sealed class CorkboardService
{
    /// <summary>
    /// How far apart cards are placed when a scene has never been positioned.
    /// Matches the card size and gap in the renderer so a fresh board reads as
    /// a tidy grid rather than an overlapping stack.
    /// </summary>
    internal const int CardWidth = 220;
    internal const int CardHeight = 160;
    internal const int Gap = 24;
    internal const int Columns = 4;

    /// <summary>Cards cannot be dragged off the top or left edge and lost.</summary>
    internal const int MinCoordinate = 0;

    /// <summary>
    /// Far enough for any arrangement, near enough that a stray drag or a bad
    /// value cannot put a card somewhere no scrollbar will reach.
    /// </summary>
    internal const int MaxCoordinate = 20000;

    private readonly IProjectService _projects;

    public CorkboardService(IProjectService projects)
    {
        _projects = projects;
    }

    /// <summary>
    /// Every card's place, in reading order. A scene that has never been placed
    /// gets the slot it would have had in the default grid, so a board opened
    /// for the first time is the book in order rather than everything at once
    /// in the corner.
    /// </summary>
    public IReadOnlyList<CardPlacement> Placements()
    {
        var placements = new List<CardPlacement>();
        var index = 0;
        foreach (var chapter in _projects.GetChaptersOrdered())
        {
            foreach (var scene in _projects.GetScenesForChapter(chapter.Guid).OrderBy(s => s.Order))
            {
                placements.Add(new CardPlacement(
                    scene.Id,
                    scene.BoardX ?? DefaultX(index),
                    scene.BoardY ?? DefaultY(index)));
                index++;
            }
        }
        return placements;
    }

    internal static int DefaultX(int index) => (index % Columns) * (CardWidth + Gap);

    internal static int DefaultY(int index) => (index / Columns) * (CardHeight + Gap);

    /// <summary>
    /// Puts one card down. Returns false when the scene is unknown. Coordinates
    /// are clamped rather than refused: a drag that ends off the board is still
    /// a drag the writer meant, and losing the card would be the worse answer.
    /// </summary>
    public async Task<bool> SetPositionAsync(string sceneId, int x, int y)
    {
        var scene = FindScene(sceneId);
        if (scene == null) return false;

        scene.BoardX = Math.Clamp(x, MinCoordinate, MaxCoordinate);
        scene.BoardY = Math.Clamp(y, MinCoordinate, MaxCoordinate);
        await _projects.SaveScenesAsync();
        return true;
    }

    /// <summary>
    /// Forgets every placement, so the board falls back to reading order. The
    /// way out of an arrangement that stopped making sense, without dragging
    /// every card back by hand.
    /// </summary>
    public async Task<int> ResetAsync()
    {
        var cleared = 0;
        foreach (var scene in AllScenes())
        {
            if (scene.BoardX == null && scene.BoardY == null) continue;
            scene.BoardX = null;
            scene.BoardY = null;
            cleared++;
        }

        if (cleared > 0) await _projects.SaveScenesAsync();
        return cleared;
    }

    private IEnumerable<SceneData> AllScenes()
    {
        var manifest = _projects.ScenesManifest;
        if (manifest == null) yield break;
        foreach (var scene in manifest.Chapters.SelectMany(c => c.Value)) yield return scene;
    }

    private SceneData? FindScene(string sceneId)
        => AllScenes().FirstOrDefault(s => s.Id == sceneId);
}
