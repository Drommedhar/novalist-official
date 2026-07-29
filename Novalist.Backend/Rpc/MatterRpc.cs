using Novalist.Core.Models;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Front- and back-matter pages: half title, copyright, dedication and so on.</summary>
public sealed class MatterRpc
{
    private readonly Workspace _workspace;

    public MatterRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    /// <summary>Every kind the writer can add, in the order the UI offers them.</summary>
    [JsonRpcMethod("matter/kinds")]
    public string[] Kinds() => Enum.GetNames<BookMatterKind>();

    [JsonRpcMethod("matter/list")]
    public MatterDto[] List() =>
        (_workspace.Projects.ActiveBook?.Matter ?? [])
            .OrderBy(m => m.Placement)
            .ThenBy(m => m.Order)
            .Select(ToDto)
            .ToArray();

    /// <summary>
    /// Adds a page of the given kind. Placement and table-of-contents listing
    /// start from what that kind conventionally does, so the common case needs
    /// no further configuration.
    /// </summary>
    [JsonRpcMethod("matter/create")]
    public async Task<MatterDto[]> CreateAsync(string kind)
    {
        var book = _workspace.Projects.ActiveBook
            ?? throw new InvalidOperationException("No active book.");

        if (!Enum.TryParse<BookMatterKind>(kind, ignoreCase: true, out var parsed))
            parsed = BookMatterKind.Custom;

        var placement = BookMatterElement.DefaultPlacement(parsed);
        book.Matter.Add(new BookMatterElement
        {
            Kind = parsed,
            Placement = placement,
            InTableOfContents = BookMatterElement.ListedInTableOfContentsByDefault(parsed),
            Order = book.Matter.Count(m => m.Placement == placement)
        });

        await _workspace.Projects.SaveProjectAsync();
        return List();
    }

    [JsonRpcMethod("matter/update")]
    public async Task<MatterDto[]> UpdateAsync(
        string id, string? title, string? content, bool? included, bool? inTableOfContents, string? placement)
    {
        var element = Find(id);
        if (element == null)
            return List();

        if (title != null) element.Title = title;
        if (content != null) element.Content = content;
        if (included.HasValue) element.Included = included.Value;
        if (inTableOfContents.HasValue) element.InTableOfContents = inTableOfContents.Value;
        if (placement != null && Enum.TryParse<BookMatterPlacement>(placement, ignoreCase: true, out var parsed))
            element.Placement = parsed;

        await _workspace.Projects.SaveProjectAsync();
        return List();
    }

    /// <summary>Moves a page up or down within its own placement group.</summary>
    [JsonRpcMethod("matter/reorder")]
    public async Task<MatterDto[]> ReorderAsync(string id, int delta)
    {
        var book = _workspace.Projects.ActiveBook;
        var element = Find(id);
        if (book == null || element == null || delta == 0)
            return List();

        var siblings = book.Matter
            .Where(m => m.Placement == element.Placement)
            .OrderBy(m => m.Order)
            .ToList();

        var index = siblings.IndexOf(element);
        var target = index + delta;
        if (target < 0 || target >= siblings.Count)
            return List();

        siblings.RemoveAt(index);
        siblings.Insert(target, element);
        for (var i = 0; i < siblings.Count; i++)
            siblings[i].Order = i;

        await _workspace.Projects.SaveProjectAsync();
        return List();
    }

    [JsonRpcMethod("matter/delete")]
    public async Task<MatterDto[]> DeleteAsync(string id)
    {
        var book = _workspace.Projects.ActiveBook;
        var element = Find(id);
        if (book == null || element == null)
            return List();

        book.Matter.Remove(element);
        await _workspace.Projects.SaveProjectAsync();
        return List();
    }

    private BookMatterElement? Find(string id) =>
        _workspace.Projects.ActiveBook?.Matter
            .FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));

    private static MatterDto ToDto(BookMatterElement m) =>
        new(
            m.Id,
            m.Kind.ToString(),
            m.Placement.ToString(),
            m.Title,
            m.Content,
            m.Order,
            m.Included,
            m.InTableOfContents,
            BookMatterElement.ShowsHeadingByDefault(m.Kind));
}

public sealed record MatterDto(
    string Id,
    string Kind,
    string Placement,
    string Title,
    string Content,
    int Order,
    bool Included,
    bool InTableOfContents,
    bool ShowsHeadingByDefault);
