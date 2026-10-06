using System.Text;
using Novalist.Sdk;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;
using Novalist.Sdk.Models.Narration;
using Novalist.Sdk.Services;

namespace Novalist.Sdk.Example;

public sealed partial class WritingToolkitExtension
{
    public Task<GrammarCheckResult> CheckAsync(string plainText, string language, CancellationToken cancellationToken = default)
    {
        // Example: flags the cliché "very unique". A real contributor would do
        // more; this keeps the sample dependency-free and deterministic.
        var issues = new List<GrammarIssue>();
        var idx = plainText.IndexOf("very unique", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            issues.Add(new GrammarIssue
            {
                Offset = idx,
                Length = "very unique".Length,
                Message = _loc.T("grammar.veryUnique"),
                Type = GrammarIssueType.Style,
                Replacements = ["unique"]
            });
        }
        return Task.FromResult(new GrammarCheckResult { Issues = issues });
    }

    public Task<ArticleGenerationResult> GenerateAsync(
        ArticleGenerationRequest request, CancellationToken cancellationToken = default)
    {
        // Deterministic stand-in for a real model. An entity named "GenFail"
        // exercises the error path; everything else returns a one-line summary.
        if (string.Equals(request.EntityName, "GenFail", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new ArticleGenerationResult { Error = "no model configured" });

        // A section request answers about that section, and says whether it was
        // told what the section already held - which is what a re-roll turns on.
        if (request.SectionTitle.Length > 0)
        {
            var reroll = request.SectionContent.Length > 0 ? " (again)" : string.Empty;
            return Task.FromResult(new ArticleGenerationResult
            {
                Summary = $"On {request.SectionTitle}{reroll}: {request.EntityName} is a notable "
                    + $"{request.TypeKey} in this story."
            });
        }

        return Task.FromResult(new ArticleGenerationResult
        {
            Summary = $"{request.EntityName} is a notable {request.TypeKey} in this story."
        });
    }

    public Task<VoiceEngineStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        => _voice.GetStatusAsync(cancellationToken);

    public Task PrepareAsync(
        IProgress<VoiceEnginePrepare>? progress = null,
        CancellationToken cancellationToken = default)
        => _voice.PrepareAsync(progress, cancellationToken);

    public Task<VoiceDesignResult> DesignVoiceAsync(
        VoiceBrief brief, CancellationToken cancellationToken = default)
        => _voice.DesignVoiceAsync(brief, cancellationToken);

    public IAsyncEnumerable<NarrationClip> RenderAsync(
        NarrationRequest request, CancellationToken cancellationToken = default)
        => _voice.RenderAsync(request, cancellationToken);

    public Task ForgetVoiceAsync(string voiceId, CancellationToken cancellationToken = default)
        => _voice.ForgetVoiceAsync(voiceId, cancellationToken);

    public Task<EntityExtractionResult> ExtractAsync(
        EntityExtractionRequest request, CancellationToken cancellationToken = default)
    {
        // Deterministic stand-in for a real model: prose containing "ExtractFail"
        // exercises the error path. Otherwise every capitalised word the project
        // does not already know is proposed as a character — crude, but enough to
        // drive the host's review flow end to end.
        if (request.Context.Contains("ExtractFail", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new EntityExtractionResult { Error = "no model configured" });

        var known = new HashSet<string>(request.KnownNames, StringComparer.OrdinalIgnoreCase);
        var proposals = new List<EntityProposal>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in SplitWords(request.Context))
        {
            if (word.Length < 2 || !char.IsUpper(word[0])) continue;
            if (known.Contains(word) || !seen.Add(word)) continue;
            proposals.Add(new EntityProposal
            {
                TypeKey = "character",
                Name = word,
                Detail = "Mentioned in this scene."
            });
        }
        return Task.FromResult(new EntityExtractionResult { Proposals = proposals });
    }

    /// <summary>Letter runs only, so no escaped separator literals are needed.</summary>
    private static IEnumerable<string> SplitWords(string text)
    {
        var buffer = new System.Text.StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetter(ch)) { buffer.Append(ch); continue; }
            if (buffer.Length > 0) { yield return buffer.ToString(); buffer.Clear(); }
        }
        if (buffer.Length > 0) yield return buffer.ToString();
    }
}
