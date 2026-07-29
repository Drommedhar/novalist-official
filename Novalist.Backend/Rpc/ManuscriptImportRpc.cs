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
    public string[] Formats()
        => [.. ManuscriptReader.SupportedExtensions, ScrivenerReader.ProjectExtension];

    /// <summary>
    /// What importing this file would create. Reads and splits without writing
    /// anything, so it is safe to run on the wrong file.
    /// </summary>
    [JsonRpcMethod("manuscriptImport/preview")]
    public ImportPlanDto Preview(string path)
    {
        // A Scrivener project is a folder rather than a file, and its binder
        // already says where the chapters are - so it never goes through the
        // heading-guessing splitter.
        if (ScrivenerReader.LooksLikeScrivener(path))
            return ScrivenerPlan(ScrivenerReader.Read(path));

        return ToDto(ManuscriptSplitter.Split(ManuscriptReader.Read(path)));
    }

    /// <summary>
    /// The Scrivener binder as chapters and scenes, plus what the import will
    /// leave behind. Novalist has no equivalent for a label, a collection or a
    /// snapshot history, and a silent import that drops a research folder is
    /// worse than one that says so first.
    /// </summary>
    private static ImportPlanDto ScrivenerPlan(ScrivenerProject project)
    {
        var chapters = project.Scenes
            .GroupBy(sc => sc.ChapterTitle, StringComparer.Ordinal)
            .Select(g => new ImportChapterDto(
                g.Key,
                [.. g.Select(sc => new ImportSceneDto(sc.Title, WordsIn(sc.Text)))]))
            .ToArray();

        return new ImportPlanDto(
            project.Version.Length > 0 ? $"scrivener{project.Version}" : string.Empty,
            chapters.Length,
            project.Scenes.Count,
            project.Scenes.Sum(sc => WordsIn(sc.Text)),
            chapters,
            [.. project.Losses]);
    }

    private static int WordsIn(string text) => Workspace.CountWords(text);

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

        if (ScrivenerReader.LooksLikeScrivener(path))
            return await RunScrivenerAsync(ScrivenerReader.Read(path));

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

    /// <summary>
    /// Creates the chapters and scenes a Scrivener binder describes. Synopsis
    /// cards come across as scene synopses, which is the one piece of Scrivener
    /// metadata Novalist has an exact home for.
    /// </summary>
    private async Task<ImportResultDto> RunScrivenerAsync(ScrivenerProject project)
    {
        if (project.IsEmpty) return new ImportResultDto(0, 0, 0);

        var chapters = 0;
        var scenes = 0;
        var words = 0;

        foreach (var group in project.Scenes.GroupBy(sc => sc.ChapterTitle, StringComparer.Ordinal))
        {
            var chapter = await _workspace.Projects.CreateChapterAsync(group.Key);
            chapters++;

            foreach (var imported in group)
            {
                var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, imported.Title);
                var html = ParagraphsToHtml(imported.Text);
                await _workspace.WriteSceneAsync(chapter.Guid, scene.Id, html, imported.Text);
                if (imported.Synopsis.Length > 0) scene.Synopsis = imported.Synopsis;
                words += WordsIn(imported.Text);
                scenes++;
            }
        }

        await _workspace.Projects.SaveScenesAsync();
        return new ImportResultDto(chapters, scenes, words);
    }

    /// <summary>Blank-line-separated prose as the paragraph markup the editor
    /// speaks.</summary>
    private static string ParagraphsToHtml(string text)
        => string.Concat(text
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .Select(p => "<p>" + System.Net.WebUtility.HtmlEncode(p) + "</p>"));

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
                .ToArray(),
            []);
}

public sealed record ImportSceneDto(string Title, int WordCount);

public sealed record ImportChapterDto(string Title, ImportSceneDto[] Scenes);

/// <summary>
/// What an import would create. <c>Losses</c> names what will not come across -
/// empty for the single-file formats, populated for a Scrivener project, whose
/// research folders and metadata Novalist has no home for.
/// </summary>
public sealed record ImportPlanDto(
    string Format, int ChapterCount, int SceneCount, int WordCount, ImportChapterDto[] Chapters,
    string[] Losses);

public sealed record ImportResultDto(int Chapters, int Scenes, int Words);
