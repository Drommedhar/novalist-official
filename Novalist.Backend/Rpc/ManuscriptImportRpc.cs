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
public sealed class ManuscriptImportRpc
{
    private readonly Workspace _workspace;

    public ManuscriptImportRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    /// <summary>File extensions the importer can read.</summary>
    [JsonRpcMethod("manuscriptImport/formats")]
    public string[] Formats() => ManuscriptReader.SupportedExtensions.ToArray();

    /// <summary>
    /// What importing this file would create. Reads and splits without writing
    /// anything, so it is safe to run on the wrong file.
    /// </summary>
    [JsonRpcMethod("manuscriptImport/preview")]
    public ImportPlanDto Preview(string path)
    {
        var plan = ManuscriptSplitter.Split(ManuscriptReader.Read(path));
        return ToDto(plan);
    }

    /// <summary>
    /// Creates the chapters and scenes from a previously previewed file.
    /// Everything is appended - an import never replaces what is already in the
    /// book, so running it twice duplicates rather than destroys.
    /// </summary>
    [JsonRpcMethod("manuscriptImport/run")]
    public async Task<ImportResultDto> RunAsync(string path)
    {
        if (_workspace.Projects.ActiveBook == null)
            throw new InvalidOperationException("No project open.");

        var plan = ManuscriptSplitter.Split(ManuscriptReader.Read(path));
        if (plan.IsEmpty)
            return new ImportResultDto(0, 0, 0);

        var chapters = 0;
        var scenes = 0;

        foreach (var importedChapter in plan.Chapters)
        {
            var chapter = await _workspace.Projects.CreateChapterAsync(importedChapter.Title);
            chapters++;

            foreach (var importedScene in importedChapter.Scenes)
            {
                var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, importedScene.Title);
                await _workspace.WriteSceneAsync(
                    chapter.Guid, scene.Id, importedScene.Html, PlainTextOf(importedScene.Html));
                scenes++;
            }
        }

        return new ImportResultDto(chapters, scenes, plan.WordCount);
    }

    /// <summary>Paragraph text without the tags, for the word count the
    /// manifest stores.</summary>
    private static string PlainTextOf(string html) =>
        Novalist.Core.Utilities.TextDiff.StripHtml(html);

    private static ImportPlanDto ToDto(ImportPlan plan) =>
        new(
            plan.Format,
            plan.Chapters.Count,
            plan.SceneCount,
            plan.WordCount,
            plan.Chapters
                .Select(c => new ImportChapterDto(
                    c.Title,
                    c.Scenes.Select(s => new ImportSceneDto(s.Title, s.WordCount)).ToArray()))
                .ToArray());
}

public sealed record ImportSceneDto(string Title, int WordCount);

public sealed record ImportChapterDto(string Title, ImportSceneDto[] Scenes);

public sealed record ImportPlanDto(
    string Format, int ChapterCount, int SceneCount, int WordCount, ImportChapterDto[] Chapters);

public sealed record ImportResultDto(int Chapters, int Scenes, int Words);
