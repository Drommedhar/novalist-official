using System.Threading;
using System.Threading.Tasks;

namespace Novalist.Sdk.Hooks;

/// <summary>
/// Allows an extension (typically an AI assistant) to generate a short
/// encyclopedic summary for a Codex entity, shown at the top of that entity's
/// read-only Wiki article. The host builds a deterministic, plain-text dossier
/// of the entity — its fields, sections, relationships, and the scenes it
/// appears in — and passes it as <see cref="ArticleGenerationRequest.Context"/>;
/// the contributor turns that into prose. The core app never calls an AI service
/// itself, so this seam carries no AI dependency: the contributor owns its own
/// model access.
/// </summary>
public interface IArticleGeneratorContributor
{
    /// <summary>Unique name for this generator (shown in diagnostics).</summary>
    string ArticleGeneratorName { get; }

    /// <summary>Whether this generator is currently usable (e.g. a model is
    /// configured and reachable). The Wiki hides the generate action when no
    /// enabled generator exists.</summary>
    bool IsArticleGeneratorEnabled { get; }

    /// <summary>
    /// Produces a summary for the entity described by <paramref name="request"/>.
    /// Return prose in <see cref="ArticleGenerationResult.Summary"/>, or a
    /// short human-readable reason in <see cref="ArticleGenerationResult.Error"/>.
    /// </summary>
    /// <param name="request">The entity identity and its plain-text dossier.</param>
    /// <param name="cancellationToken">Cancelled if the user aborts generation.</param>
    Task<ArticleGenerationResult> GenerateAsync(
        ArticleGenerationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>The input to <see cref="IArticleGeneratorContributor.GenerateAsync"/>.</summary>
public sealed class ArticleGenerationRequest
{
    /// <summary>Entity type key ("character", "location", … or a custom type).</summary>
    public string TypeKey { get; init; } = string.Empty;

    /// <summary>The entity's id.</summary>
    public string EntityId { get; init; } = string.Empty;

    /// <summary>The entity's display name (for the prompt's subject).</summary>
    public string EntityName { get; init; } = string.Empty;

    /// <summary>A deterministic, plain-text dossier assembled by the host from the
    /// entity's fields, sections, relationships, and appearances — everything the
    /// generator needs, so it does not re-query the project.</summary>
    public string Context { get; init; } = string.Empty;

    /// <summary>
    /// The one section being written, or empty for the whole-entity summary the
    /// Wiki shows.
    ///
    /// A single summary is regenerated whole or not at all, which is the wrong
    /// unit for the way an entry is actually filled in: the writer is happy with
    /// the history and wants another go at the appearance. The title is the
    /// writer's own — "Backstory", "How they speak" — and is the strongest hint
    /// there is about what belongs there.
    /// </summary>
    public string SectionTitle { get; init; } = string.Empty;

    /// <summary>
    /// What that section says now, empty when it is blank. A re-roll that cannot
    /// see what it is replacing tends to hand back the same paragraph again.
    /// </summary>
    public string SectionContent { get; init; } = string.Empty;
}

/// <summary>The result of a generation attempt.</summary>
public sealed class ArticleGenerationResult
{
    /// <summary>The generated summary prose (plain text or light Markdown).</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>A short reason the summary could not be produced, if any. When
    /// set, <see cref="Summary"/> is ignored.</summary>
    public string? Error { get; init; }
}
