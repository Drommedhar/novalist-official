using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Craft reference: something to write, specifics to describe with, and short
/// pieces on the craft itself.
///
/// All of it is static content, so none of these touch the project. They are
/// here rather than bundled into the renderer so the same words reach an
/// extension, an export, and any future surface without being written twice.
/// </summary>
public sealed class CraftRpc
{
    /// <summary>A prompt by index, wrapping. The caller owns the number so a
    /// writer who liked one can get it back.</summary>
    [JsonRpcMethod("craft/prompt")]
    public CraftPromptDto? Prompt(int index, string? kind = null)
    {
        var prompt = CraftLibrary.PromptAt(index, kind);
        return prompt == null ? null : new CraftPromptDto(prompt.Id, prompt.Kind, prompt.Text);
    }

    /// <summary>The kinds a prompt can be, so the interface needs no second list.</summary>
    [JsonRpcMethod("craft/promptKinds")]
    public string[] PromptKinds()
        => [.. CraftLibrary.Prompts.Select(p => p.Kind).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// Thesaurus entries matching a query, by name or by any of their signals.
    /// An empty query returns everything: the list is short enough to browse,
    /// and browsing is how a writer finds the entry they did not know to want.
    /// </summary>
    [JsonRpcMethod("craft/lookup")]
    public CraftEntryDto[] Lookup(string? query = null)
        => [.. CraftLibrary.Search(query)
            .Select(e => new CraftEntryDto(e.Key, e.Group, e.Name, [.. e.Signals]))];

    /// <summary>Every article, without its body - a list to choose from.</summary>
    [JsonRpcMethod("craft/articles")]
    public CraftArticleSummaryDto[] Articles()
        => [.. CraftLibrary.Articles.Select(a => new CraftArticleSummaryDto(a.Id, a.Topic, a.Title))];

    /// <summary>One article to read. Null when the id is unknown.</summary>
    [JsonRpcMethod("craft/article")]
    public CraftArticleDto? Article(string id)
    {
        var article = CraftLibrary.Article(id);
        return article == null
            ? null
            : new CraftArticleDto(article.Id, article.Topic, article.Title, article.Body);
    }
}

/// <summary>Something to write.</summary>
public sealed record CraftPromptDto(string Id, string Kind, string Text);

/// <summary>A thing to describe, and specifics to describe it with.</summary>
public sealed record CraftEntryDto(
    string Key, string Group, string Name, IReadOnlyList<string> Signals);

/// <summary>An article in a list.</summary>
public sealed record CraftArticleSummaryDto(string Id, string Topic, string Title);

/// <summary>An article to read.</summary>
public sealed record CraftArticleDto(string Id, string Topic, string Title, string Body);
