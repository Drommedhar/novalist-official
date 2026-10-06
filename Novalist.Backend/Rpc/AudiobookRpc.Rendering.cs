using Novalist.Backend.Extensions;
using Novalist.Core.Services;
using Novalist.Sdk.Hooks;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class AudiobookRpc
{
    private async Task RunAsync(
        NarrationRenderJob job,
        IReadOnlyList<NarrationRenderChapter> chapters,
        VoiceCastSheet sheet,
        IReadOnlyDictionary<string, byte[]> voices,
        IReadOnlyDictionary<string, string> voiceReferenceTexts,
        IReadOnlyDictionary<string, byte[]> references,
        IVoiceEngineContributor engine,
        AudiobookFormat format,
        string outputPath,
        double rate,
        string language,
        AudiobookMetadata metadata,
        CancellationTokenSource cancellation)
    {
        try
        {
            var outcome = await job.RunAsync(
                chapters,
                new NarrationRenderContext(sheet, voices, engine.Features, language)
                {
                    Clips = references,
                    VoiceReferenceTexts = voiceReferenceTexts
                },
                new NarrationRenderSettings { Rate = rate },
                new Inline(report =>
                {
                    lock (_gate)
                    {
                        _state = _state with
                        {
                            ChapterIndex = report.ChapterIndex,
                            ChapterCount = report.ChapterCount,
                            ChapterTitle = report.ChapterTitle,
                            SegmentsDone = report.SegmentsDone,
                            SegmentsTotal = report.SegmentsTotal,
                            AudioMs = report.AudioMs,
                            ElapsedMs = report.ElapsedMs
                        };
                    }
                }),
                cancellation.Token);

            // Only a finished render says anything about how fast this machine
            // is. A stopped one is a partial measurement of an unknown fraction.
            if (outcome.Completed)
                _speed.Record(outcome.AudioMs, outcome.ElapsedMs);

            lock (_gate)
            {
                _state = _state with
                {
                    Phase = "packaging",
                    AudioMs = outcome.AudioMs,
                    ElapsedMs = outcome.ElapsedMs,
                    Missing = outcome.Chapters.Sum(c => c.Missing)
                };
            }

            var result = await _packager.PackageAsync(
                job.Folder, outcome.Chapters, format, outputPath, metadata, CancellationToken.None);

            lock (_gate)
            {
                _state = _state with
                {
                    Phase = outcome.Completed ? "done" : "stopped",
                    Files = [.. result.Files],
                    DeliveredFormat = result.Format.ToString(),
                    Note = result.Note,
                    Error = outcome.Error
                };
            }

            Log.Info(
                $"audiobook rendered chapters={outcome.Chapters.Count} " +
                $"reused={outcome.Chapters.Count(c => c.Reused)} " +
                $"missing={outcome.Chapters.Sum(c => c.Missing)} " +
                $"completed={outcome.Completed} delivered={result.Format} note={result.Note ?? "-"}.");
        }
        catch (Exception ex)
        {
            // By type. An engine's message can carry the line it choked on.
            Log.Warn($"audiobook failed type={ex.GetType().Name}.");
            lock (_gate)
            {
                _state = _state with { Phase = "failed", Error = ex.GetType().Name };
            }
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_running, cancellation))
                    _running = null;
            }
            cancellation.Dispose();
        }
    }

    /// <summary>
    /// The book as the render job wants it: the export's own compile, with each
    /// scene's cast and directions put back.
    /// </summary>
    private async Task<IReadOnlyList<NarrationRenderChapter>> ChaptersAsync(string[]? selected)
    {
        var projects = _workspace.Projects;
        var book = projects.ActiveBook;
        if (book == null)
            return [];

        var (compiled, options) = await CompileBookAsync(projects, book, selected);

        var characters = await _entities.LoadCharactersAsync();
        var lexicon = SceneAnalysisLexicon.For(WritingLanguage());
        var candidates = DialogueAttributor.BuildCandidates(
            characters, lexicon?.WordBoundaries ?? true);
        var dialogueLanguage = DialogueAttributor.BuildLanguage(lexicon);
        var directionLanguage = EmotionDirector.BuildLanguage(lexicon);
        // What tells a sentence ending from a full stop that is merely a full
        // stop. Without it every point is an ending, and "10 a.m. sharp." is
        // three things for a model to say rather than one.
        var utteranceLanguage = UtteranceLanguage.From(lexicon);
        var manifest = projects.ScenesManifest;
        // Which act each chapter is in. The compiled chapter is an export
        // record and does not carry it, and an act is a stretch a writer can
        // set a voice over.
        var act = (projects.ActiveBook?.Chapters ?? [])
            .Where(c => !string.IsNullOrEmpty(c.Guid))
            .ToDictionary(c => c.Guid, c => c.Act, StringComparer.Ordinal);

        // The pages around the story, spoken like everything else. A recorded
        // book reads its dedication; leaving them out would make the audiobook
        // the one edition that quietly drops what the writer wrote to open it.
        var chapters = new List<NarrationRenderChapter>(compiled.Count + options.Matter.Count);
        chapters.AddRange(Matter(
            options, "Front", candidates, dialogueLanguage, directionLanguage,
            utteranceLanguage));

        foreach (var chapter in compiled)
        {
            var scenes = new List<NarrationRenderScene>(chapter.Scenes.Count);
            foreach (var scene in chapter.Scenes)
            {
                // The overrides live on the project's scene, and the compiled
                // one is a copy with the replacements already run. Matching by
                // id is what keeps a hand-cast line cast after a compile.
                var source = manifest?.Chapters.GetValueOrDefault(chapter.Guid)
                    ?.FirstOrDefault(s => s.Id == scene.Id);

                scenes.Add(new NarrationRenderScene(
                    scene.Id,
                    NarrationScript.Build(
                        scene.HtmlContent,
                        candidates,
                        new NarrationLanguageContext(dialogueLanguage, directionLanguage, utteranceLanguage),
                        new SceneNarrationSettings()
                        {
                            SpeakerOverrides = source?.DialogueSpeakers,
                            DirectionOverrides = source?.DialogueDirections,
                            Emotion = source?.AnalysisOverrides?.Emotion,
                            Intensity = source?.AnalysisOverrides?.Intensity
                        }),
                    // Where in the book, so a voice the writer set over an act
                    // or a chapter is the voice that gets recorded. Without it
                    // the export is the one place a wrong voice is baked into a
                    // file somebody then publishes.
                    new NarrationPlacement(
                        act.GetValueOrDefault(chapter.Guid),
                        chapter.Guid,
                        chapter.Title,
                        source?.Title)));
            }

            chapters.Add(new NarrationRenderChapter(
                string.IsNullOrEmpty(chapter.Guid) ? chapter.Title : chapter.Guid,
                chapter.Heading.Length > 0 ? chapter.Heading : chapter.Title,
                scenes));
        }

        chapters.AddRange(Matter(
            options, "Back", candidates, dialogueLanguage, directionLanguage,
            utteranceLanguage));
        return chapters;
    }

    private async Task<(List<ChapterExportContent> Chapters, ExportOptions Options)> CompileBookAsync(
        IProjectService projects, Novalist.Core.Models.BookData book, string[]? selected)
    {
        var service = new ExportService(projects, _entities);
        var options = new ExportOptions
        {
            Format = ExportFormat.Markdown,
            Title = book.Name,
            SelectedChapterGuids = selected is { Length: > 0 }
                ? [.. selected]
                : [.. book.Chapters.Select(c => c.Guid)],
            Language = ExportService.NormalizeLanguageTag(
                _workspace.Settings.Effective.AutoReplacementLanguage),
            CustomPresets = [.. book.ExportPresets ?? []]
        };
        var compiled = await service.CompileChaptersAsync(options);

        return (compiled, options);
    }

    /// <summary>
    /// The front or back matter, each page as a chapter of its own.
    ///
    /// Its own chapter rather than folded into the first or last, so a player's
    /// chapter list names it and a listener can skip a copyright page the way
    /// they skip anything else. Read by the narrator: a matter page has no
    /// dialogue to attribute and no scene to take an emotion from.
    /// </summary>
    private static IEnumerable<NarrationRenderChapter> Matter(
        ExportOptions options,
        string placement,
        IReadOnlyList<DialogueSpeakerCandidate> candidates,
        DialogueLanguage dialogueLanguage,
        DirectionLanguage directionLanguage,
        UtteranceLanguage utteranceLanguage)
    {
        foreach (var page in options.Matter
            .Where(m => string.Equals(m.Placement, placement, StringComparison.Ordinal))
            .OrderBy(m => m.Order))
        {
            var segments = NarrationScript.Build(
                page.HtmlContent,
                candidates,
                new NarrationLanguageContext(dialogueLanguage, directionLanguage, utteranceLanguage));
            if (segments.Count == 0)
                continue;

            yield return new NarrationRenderChapter(
                $"matter:{page.Id}",
                page.Title.Length > 0 ? page.Title : page.Kind,
                [new NarrationRenderScene(page.Id, segments)]);
        }
    }

    private AudiobookMetadata MetadataFor()
    {
        var book = _workspace.Projects.ActiveBook;
        return new AudiobookMetadata
        {
            Title = book?.Name ?? string.Empty,
            Author = book?.Author ?? string.Empty,
            Description = book?.Premise?.Logline ?? string.Empty,
            Language = ExportService.NormalizeLanguageTag(
                _workspace.Settings.Effective.AutoReplacementLanguage),
            Year = DateTime.Now.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CoverPath = _workspace.ActiveCoverAbsolutePath() ?? string.Empty
        };
    }

    /// <summary>Where chapter audio goes, or null with no project open.</summary>
    private string? RenderRoot()
    {
        var root = _workspace.Projects.ProjectRoot;
        return root == null
            ? null
            : Path.Combine(root, ".novalist", "narration", RenderFolder);
    }
}
