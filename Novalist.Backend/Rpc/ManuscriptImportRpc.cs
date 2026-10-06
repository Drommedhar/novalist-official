using System.Diagnostics;
using Novalist.Backend.Extensions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Importing an existing manuscript from the formats writers arrive with.
///
/// Deliberately two calls: preview shows what would be created without touching
/// the project, and commit does it. Dropping someone's whole book into a project
/// is not something to do without showing them the plan first.
/// </summary>
public sealed partial class ManuscriptImportRpc
{
    private readonly Workspace _workspace;

    public ManuscriptImportRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    /// <summary>File extensions the importer can read.</summary>
    [JsonRpcMethod("manuscriptImport/formats")]
    public string[] Formats()
        => [.. ManuscriptReader.SupportedExtensions,
            ScrivenerReader.ProjectExtension,
            ScrivenerReader.BinderExtension];

    /// <summary>Records a content-free failure that happened in the native
    /// picker before the selected source could reach this backend.</summary>
    [JsonRpcMethod("manuscriptImport/pickerFailure")]
    public void PickerFailure(string? stage, string? reason)
    {
        var safeStage = stage is "project" or "source" ? stage : "unknown";
        var safeReason = reason is "access-denied" or "disk-full" or "source-missing"
            or "unsafe-link" or "manifest-not-found" or "manifest-ambiguous"
            or "invalid-manifest" or "invalid-project" or "io" or "other" ? reason : "other";
        Log.Warn($"manuscriptImport/picker failed stage={safeStage} reason={safeReason}.");
    }

    /// <summary>
    /// What importing this file would create. Reads and splits without writing
    /// anything, so it is safe to run on the wrong file.
    /// </summary>
    [JsonRpcMethod("manuscriptImport/preview")]
    public ImportPlanDto Preview(string path, ImportMappingDto[]? mapping = null)
    {
        var started = Stopwatch.GetTimestamp();
        var source = SourceShape(path);
        var extension = SourceExtension(path);
        var isScrivener = ScrivenerReader.LooksLikeScrivener(path);
        Log.Info(
            $"manuscriptImport/preview start source={source} extension={extension} " +
            $"scrivener={isScrivener} mapping={mapping?.Length ?? 0}.");

        ImportPlanDto plan;
        // A Scrivener folder or exact binder already says where the chapters
        // are, so it never goes through the heading-guessing splitter.
        if (isScrivener)
        {
            var chosen = MappingFrom(mapping);
            // Read and Outline both open the package. The same parse failure is
            // one diagnostic event, not two identical warnings a millisecond
            // apart.
            var diagnostics = new HashSet<ScrivenerReadDiagnostic>();
            void Diagnostic(ScrivenerReadDiagnostic diagnostic)
            {
                if (diagnostics.Add(diagnostic))
                    LogScrivenerDiagnostic("preview", diagnostic);
            }
            plan = ScrivenerPlan(
                ScrivenerReader.Read(path, chosen, Diagnostic),
                // The rows are what the rules found, so the dialog can offer the
                // writer's choice over the top of them without a second read of
                // the binder deciding something different.
                ScrivenerReader.Outline(path, Diagnostic),
                chosen);
        }
        else
        {
            plan = ToDto(ManuscriptSplitter.Split(ManuscriptReader.Read(path)));
        }

        LogPreview(plan, source, extension, isScrivener, mapping?.Length ?? 0, started);
        return plan;
    }

    private static void LogPreview(
        ImportPlanDto plan,
        string source,
        string extension,
        bool isScrivener,
        int mappingCount,
        long started)
    {
        var line =
            $"manuscriptImport/preview {(HasSomething(plan) ? "complete" : "empty")} " +
            $"source={source} extension={extension} scrivener={isScrivener} mapping={mappingCount} " +
            $"format={(plan.Format.Length > 0 ? plan.Format : "unknown")} " +
            $"chapters={plan.ChapterCount} scenes={plan.SceneCount} words={plan.WordCount} " +
            $"characters={plan.CharacterCount} locations={plan.LocationCount} " +
            $"research={plan.ResearchCount} targets={plan.Targets.Length} losses={plan.Losses.Length} " +
            $"durationMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0}.";
        if (HasSomething(plan)) Log.Info(line);
        else Log.Warn(line);
    }

    private static bool HasSomething(ImportPlanDto plan)
        => plan.ChapterCount > 0
            || plan.CharacterCount > 0
            || plan.LocationCount > 0
            || plan.ResearchCount > 0;

    /// <summary>
    /// Parser-owned stage/reason values, exception types and fixed diagnostic
    /// details are safe to record. Raw runtime messages never reach this layer.
    /// </summary>
    private static void LogScrivenerDiagnostic(
        string operation,
        ScrivenerReadDiagnostic diagnostic)
    {
        var location = diagnostic.LineNumber > 0
            ? $" line={diagnostic.LineNumber} position={diagnostic.LinePosition}"
            : string.Empty;
        var detail = diagnostic.Detail.Length > 0
            ? $" message=\"{diagnostic.Detail}\""
            : string.Empty;
        var code = diagnostic.ErrorCode != 0
            ? $" code=0x{unchecked((uint)diagnostic.ErrorCode):X8}"
            : string.Empty;
        Log.Warn(
            $"manuscriptImport/{operation} scrivener stage={diagnostic.Stage} " +
            $"reason={diagnostic.Reason} " +
            $"type={(diagnostic.ExceptionType.Length > 0 ? diagnostic.ExceptionType : "none")}" +
            $"{location}{code}{detail}.");
    }

    /// <summary>
    /// Creates the chapters and scenes from a previously previewed file.
    /// Everything is appended - an import never replaces what is already in the
    /// book, so running it twice duplicates rather than destroys.
    /// </summary>
    [JsonRpcMethod("manuscriptImport/run")]
    public async Task<ImportResultDto> RunAsync(string path, ImportMappingDto[]? mapping = null)
    {
        var started = Stopwatch.GetTimestamp();
        var source = SourceShape(path);
        var extension = SourceExtension(path);
        var isScrivener = false;
        var stage = "validate";
        try
        {
            if (_workspace.Projects.ActiveBook == null)
                throw new InvalidOperationException("No project open.");

            stage = "detect";
            isScrivener = ScrivenerReader.LooksLikeScrivener(path);
            Log.Info(
                $"manuscriptImport/run start source={source} extension={extension} " +
                $"scrivener={isScrivener} mapping={mapping?.Length ?? 0}.");

            ImportResultDto result;
            if (isScrivener)
            {
                stage = "read-scrivener";
                var project = ScrivenerReader.Read(
                    path,
                    MappingFrom(mapping),
                    diagnostic => LogScrivenerDiagnostic("run", diagnostic));
                result = await RunScrivenerAsync(project, next => stage = next);
            }
            else
            {
                stage = "read-manuscript";
                var plan = ManuscriptSplitter.Split(ManuscriptReader.Read(path));
                if (plan.IsEmpty)
                {
                    result = new ImportResultDto(0, 0, 0, 0, 0, 0);
                }
                else
                {
                    stage = "write-manuscript";
                    var chapters = 0;
                    var scenes = 0;

                    foreach (var importedChapter in plan.Chapters)
                    {
                        var chapter = await _workspace.Projects.CreateChapterAsync(importedChapter.Title);
                        chapters++;

                        foreach (var importedScene in importedChapter.Scenes)
                        {
                            var scene = await _workspace.Projects.CreateSceneAsync(
                                chapter.Guid, importedScene.Title);
                            await _workspace.WriteSceneAsync(
                                chapter.Guid, scene.Id, importedScene.Html, PlainTextOf(importedScene.Html));
                            scenes++;
                        }
                    }

                    result = new ImportResultDto(chapters, scenes, plan.WordCount, 0, 0, 0);
                }
            }

            LogRun(result, source, extension, isScrivener, mapping?.Length ?? 0, started);
            return result;
        }
        catch (Exception ex)
        {
            Log.Error(
                $"manuscriptImport/run failed stage={stage} source={source} extension={extension} " +
                $"scrivener={isScrivener} mapping={mapping?.Length ?? 0} " +
                $"type={ex.GetType().FullName} " +
                $"durationMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0}.");
            throw;
        }
    }

    private static void LogRun(
        ImportResultDto result,
        string source,
        string extension,
        bool isScrivener,
        int mappingCount,
        long started)
    {
        var empty = result.Chapters == 0
            && result.Scenes == 0
            && result.Characters == 0
            && result.Locations == 0
            && result.Research == 0;
        var line =
            $"manuscriptImport/run {(empty ? "empty" : "complete")} source={source} " +
            $"extension={extension} scrivener={isScrivener} mapping={mappingCount} " +
            $"chapters={result.Chapters} scenes={result.Scenes} words={result.Words} " +
            $"characters={result.Characters} locations={result.Locations} research={result.Research} " +
            $"drafts={result.Drafts} books={result.Books} " +
            $"durationMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0}.";
        if (empty) Log.Warn(line);
        else Log.Info(line);
    }
}

public sealed record ImportSceneDto(string Title, int WordCount);

/// <summary><c>PartTitle</c> is the act this chapter lands in, empty when the
/// source had no part above it.</summary>
public sealed record ImportChapterDto(string Title, string PartTitle, ImportSceneDto[] Scenes);

/// <summary>
/// What an import would create. <c>Losses</c> names what will not come across -
/// empty for the single-file formats, populated for a Scrivener project, whose
/// snapshots and compile settings Novalist has no home for.
/// </summary>
public sealed record ImportPlanDto(
    string Format, int ChapterCount, int SceneCount, int WordCount, ImportChapterDto[] Chapters,
    string[] Losses, int PartCount, int CharacterCount, int LocationCount, int ResearchCount,
    /// <summary>The binder rows the writer can redirect. Empty for the
    /// single-file formats, which have no binder to arrange.</summary>
    ImportMappingRowDto[] Mapping,
    /// <summary>The book and the drafts and books this import would create,
    /// in binder order, with what each would hold.</summary>
    ImportTargetDto[] Targets);

/// <summary>
/// One row of the Scrivener binder the writer can send somewhere of their own
/// choosing. <c>Destination</c> is one of "manuscript", "draft", "book",
/// "characters", "places", "research" or "skip".
/// </summary>
public sealed record ImportMappingRowDto(
    string Key, string Title, int Depth, string Destination, int Documents, bool HasChildren);

/// <summary>The writer's choice for one binder row, sent back with the preview
/// or the import.</summary>
public sealed record ImportMappingDto(string Key, string Destination);

/// <summary>
/// One book or draft an import would fill. <c>Kind</c> is "manuscript" for the
/// book being imported into, "draft" for a draft it would create on that book,
/// and "book" for a new book in the project.
/// </summary>
public sealed record ImportTargetDto(
    string Kind, string Title, int ChapterCount, int SceneCount, int WordCount);

public sealed record ImportResultDto(
    int Chapters, int Scenes, int Words, int Characters, int Locations, int Research,
    /// <summary>Drafts created on the active book by this import.</summary>
    int Drafts = 0,
    /// <summary>Books created in the project by this import.</summary>
    int Books = 0);
