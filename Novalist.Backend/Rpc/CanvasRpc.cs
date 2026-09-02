using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Freeform planning boards: cards and author-drawn connectors.</summary>
public sealed class CanvasRpc
{
    private readonly Workspace _workspace;

    public CanvasRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private CanvasService Service => new(_workspace.Projects, _workspace.FileService);

    [JsonRpcMethod("canvas/list")]
    public CanvasSummaryDto[] List() =>
        Service.List().Select(c => new CanvasSummaryDto(c.Id, c.Name)).ToArray();

    [JsonRpcMethod("canvas/create")]
    public async Task<CanvasDto> CreateAsync(string name) =>
        ToDto(await Service.CreateAsync(string.IsNullOrWhiteSpace(name) ? "Board" : name.Trim()));

    [JsonRpcMethod("canvas/load")]
    public async Task<CanvasDto?> LoadAsync(string canvasId)
    {
        var canvas = await Service.LoadAsync(canvasId);
        return canvas == null ? null : ToDto(canvas);
    }

    [JsonRpcMethod("canvas/save")]
    public async Task SaveAsync(CanvasDto canvas)
    {
        await Service.SaveAsync(new CanvasData
        {
            Id = canvas.Id,
            Name = canvas.Name,
            PanX = canvas.PanX,
            PanY = canvas.PanY,
            Zoom = canvas.Zoom,
            Cards = canvas.Cards.Select(c => new CanvasCard
            {
                Id = c.Id,
                Title = c.Title,
                Text = c.Text,
                X = c.X,
                Y = c.Y,
                Width = c.Width,
                Height = c.Height,
                Color = c.Color,
                SceneId = c.SceneId,
                ChapterGuid = c.ChapterGuid,
                EntityId = c.EntityId
            }).ToList(),
            Connectors = canvas.Connectors.Select(c => new CanvasConnector
            {
                Id = c.Id,
                FromCardId = c.FromCardId,
                ToCardId = c.ToCardId,
                Label = c.Label,
                FromSide = c.FromSide ?? string.Empty,
                ToSide = c.ToSide ?? string.Empty
            }).ToList()
        });
    }

    [JsonRpcMethod("canvas/delete")]
    public Task<bool> DeleteAsync(string canvasId) => Service.DeleteAsync(canvasId);

    /// <summary>
    /// Turns a card into a real scene in the given chapter and links the two, so
    /// the board keeps a pointer to where the idea ended up. This is the only
    /// place a board touches the manuscript, and it is always the writer's
    /// explicit act.
    /// </summary>
    [JsonRpcMethod("canvas/promoteCard")]
    public async Task<CanvasDto?> PromoteCardAsync(string canvasId, string cardId, string chapterGuid)
    {
        var canvas = await Service.LoadAsync(canvasId);
        var card = canvas?.Cards.FirstOrDefault(c => c.Id == cardId);
        if (canvas == null || card == null)
            return null;

        // Already promoted: the card points at a scene, so do not make a second.
        if (!string.IsNullOrEmpty(card.SceneId))
            return ToDto(canvas);

        var title = string.IsNullOrWhiteSpace(card.Title) ? "Untitled" : card.Title.Trim();
        var scene = await _workspace.Projects.CreateSceneAsync(chapterGuid, title);

        // The card's body becomes the scene's synopsis rather than its prose:
        // a planning note is a description of the scene, not the scene itself.
        if (!string.IsNullOrWhiteSpace(card.Text))
        {
            scene.Synopsis = card.Text;
            await _workspace.Projects.SaveScenesAsync();
        }

        card.SceneId = scene.Id;
        card.ChapterGuid = chapterGuid;
        await Service.SaveAsync(canvas);
        return ToDto(canvas);
    }

    private static CanvasDto ToDto(CanvasData c) =>
        new(
            c.Id,
            c.Name,
            c.PanX,
            c.PanY,
            c.Zoom,
            c.Cards.Select(card => new CanvasCardDto(
                card.Id, card.Title, card.Text, card.X, card.Y,
                card.Width, card.Height, card.Color,
                card.SceneId, card.ChapterGuid, card.EntityId)).ToArray(),
            c.Connectors.Select(conn => new CanvasConnectorDto(
                conn.Id, conn.FromCardId, conn.ToCardId, conn.Label,
                conn.FromSide ?? string.Empty, conn.ToSide ?? string.Empty)).ToArray());
}

public sealed record CanvasSummaryDto(string Id, string Name);

public sealed record CanvasCardDto(
    string Id, string Title, string Text, double X, double Y,
    double Width, double Height, string Color,
    string SceneId, string ChapterGuid, string EntityId);

public sealed record CanvasConnectorDto(
    string Id, string FromCardId, string ToCardId, string Label,
    string FromSide = "", string ToSide = "");

public sealed record CanvasDto(
    string Id, string Name, double PanX, double PanY, double Zoom,
    CanvasCardDto[] Cards, CanvasConnectorDto[] Connectors);
