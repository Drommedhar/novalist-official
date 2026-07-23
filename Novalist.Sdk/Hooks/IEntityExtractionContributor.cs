using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Novalist.Sdk.Hooks;

/// <summary>
/// Allows an extension (typically an AI assistant) to read a passage of the
/// manuscript and propose Codex entries for the people, places, and things it
/// mentions that are not in the Codex yet — closing the gap between prose the
/// writer has already written and the structured world data it implies.
///
/// The split of responsibilities mirrors
/// <see cref="IArticleGeneratorContributor"/>: the host assembles the plain-text
/// <see cref="EntityExtractionRequest.Context"/> and the list of names it already
/// knows, the contributor returns <em>proposals only</em>, and the host owns the
/// review UI and every write to the project. A contributor never creates or edits
/// entities itself, and the core app never calls an AI service — so this seam
/// carries no AI dependency and no write access.
/// </summary>
public interface IEntityExtractionContributor
{
    /// <summary>Unique name for this extractor (shown in diagnostics).</summary>
    string EntityExtractorName { get; }

    /// <summary>Whether this extractor is currently usable (e.g. a model is
    /// configured). The host hides the scan action when no enabled extractor
    /// exists.</summary>
    bool IsEntityExtractorEnabled { get; }

    /// <summary>
    /// Proposes entities found in <paramref name="request"/>. Return candidates in
    /// <see cref="EntityExtractionResult.Proposals"/>, or a short human-readable
    /// reason in <see cref="EntityExtractionResult.Error"/>. Names already listed
    /// in <see cref="EntityExtractionRequest.KnownNames"/> should be omitted.
    /// </summary>
    /// <param name="request">The passage and the names the project already has.</param>
    /// <param name="cancellationToken">Cancelled if the user aborts the scan.</param>
    Task<EntityExtractionResult> ExtractAsync(
        EntityExtractionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>The input to <see cref="IEntityExtractionContributor.ExtractAsync"/>.</summary>
public sealed class EntityExtractionRequest
{
    /// <summary>The prose to read, assembled by the host.</summary>
    public string Context { get; init; } = string.Empty;

    /// <summary>Names and aliases already in the Codex. A proposal matching one of
    /// these is redundant and should not be returned.</summary>
    public IReadOnlyList<string> KnownNames { get; init; } = [];

    /// <summary>The entity type keys the host can create ("character", "location",
    /// "item", "lore", plus any custom types). A proposal carrying any other type
    /// key is discarded by the host.</summary>
    public IReadOnlyList<string> AvailableTypeKeys { get; init; } = [];
}

/// <summary>One suggested Codex entry. Nothing is written until the writer
/// accepts it in the host's review list.</summary>
public sealed class EntityProposal
{
    /// <summary>Which kind of entry to create ("character", "location", …).</summary>
    public string TypeKey { get; init; } = string.Empty;

    /// <summary>The proposed entry name, as it appears in the prose.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>A one-line note on what the passage says about it, shown in the
    /// review list to help the writer judge the proposal.</summary>
    public string Detail { get; init; } = string.Empty;
}

/// <summary>The result of an extraction attempt.</summary>
public sealed class EntityExtractionResult
{
    /// <summary>The proposed entries, in the order they should be reviewed.</summary>
    public IReadOnlyList<EntityProposal> Proposals { get; init; } = [];

    /// <summary>A short reason the scan could not be completed, if any. When set,
    /// <see cref="Proposals"/> is ignored.</summary>
    public string? Error { get; init; }
}
