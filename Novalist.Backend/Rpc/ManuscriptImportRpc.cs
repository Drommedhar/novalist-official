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
    /// The Scrivener binder as parts, chapters and scenes, plus the Codex
    /// entries and research it will create and what it will leave behind.
    /// </summary>
    private static ImportPlanDto ScrivenerPlan(ScrivenerProject project)
    {
        var chapters = GroupChapters(project)
            .Select(g => new ImportChapterDto(
                g.Title,
                g.PartTitle,
                [.. g.Scenes.Select(sc => new ImportSceneDto(sc.Title, WordsIn(sc.Text)))]))
            .ToArray();

        return new ImportPlanDto(
            project.Version.Length > 0 ? $"scrivener{project.Version}" : string.Empty,
            chapters.Length,
            project.Scenes.Count,
            project.Scenes.Sum(sc => WordsIn(sc.Text)),
            chapters,
            [.. project.Losses],
            chapters.Select(c => c.PartTitle).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal).Count(),
            project.Entities.Count(e => e.Kind == ScrivenerEntityKind.Character),
            project.Entities.Count(e => e.Kind == ScrivenerEntityKind.Location),
            project.Research.Count);
    }

    /// <summary>
    /// The draft's documents in binder order, grouped by the chapter folder
    /// they sat in.
    ///
    /// Grouped on the folder's binder identity rather than its title: the stock
    /// Scrivener novel template names every chapter "Chapter", so grouping by
    /// title turned a four-chapter book into one chapter of four scenes.
    /// </summary>
    private static List<ScrivenerChapterGroup> GroupChapters(ScrivenerProject project)
    {
        var order = new List<string>();
        var byKey = new Dictionary<string, ScrivenerChapterGroup>(StringComparer.Ordinal);

        foreach (var scene in project.Scenes)
        {
            if (!byKey.TryGetValue(scene.ChapterKey, out var group))
            {
                group = new ScrivenerChapterGroup(
                    scene.ChapterKey, scene.ChapterTitle, scene.PartKey, scene.PartTitle);
                byKey[scene.ChapterKey] = group;
                order.Add(scene.ChapterKey);
            }

            group.Scenes.Add(scene);
        }

        return [.. order.Select(k => byKey[k])];
    }

    private sealed record ScrivenerChapterGroup(
        string Key, string Title, string PartKey, string PartTitle)
    {
        public List<ScrivenerScene> Scenes { get; } = [];
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
            return new ImportResultDto(0, 0, 0, 0, 0, 0);

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

        return new ImportResultDto(chapters, scenes, plan.WordCount, 0, 0, 0);
    }

    /// <summary>
    /// Creates everything a Scrivener project describes.
    ///
    /// Parts become acts, chapter folders become chapters, documents become
    /// scenes, and the per-document metadata Novalist has a home for comes with
    /// them: the synopsis card, the document notes, the status as a scene
    /// stage, the label as a scene label, and "include in compile" as whether
    /// the scene is exported. Character and setting sketches become Codex
    /// entries; everything else that carried content becomes research.
    /// </summary>
    private async Task<ImportResultDto> RunScrivenerAsync(ScrivenerProject project)
    {
        if (project.IsEmpty) return new ImportResultDto(0, 0, 0, 0, 0, 0);

        var book = _workspace.Projects.ActiveBook!;
        var chapters = 0;
        var scenes = 0;
        var words = 0;
        var fieldKeys = DeclareCustomFields(book, project);

        foreach (var group in GroupChapters(project))
        {
            var chapter = await _workspace.Projects.CreateChapterAsync(group.Title);
            // Scrivener's part folders are Novalist's acts: the binder groups
            // chapters under them and so does the manuscript tree.
            chapter.Act = group.PartTitle;
            if (group.PartTitle.Length > 0
                && !book.Acts.Any(a => string.Equals(a.Name, group.PartTitle, StringComparison.Ordinal)))
            {
                book.Acts.Add(new ActData { Name = group.PartTitle });
            }

            chapters++;

            foreach (var imported in group.Scenes)
            {
                var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, imported.Title);
                var html = ParagraphsToHtml(imported.Text);
                await _workspace.WriteSceneAsync(chapter.Guid, scene.Id, html, imported.Text);

                if (imported.Synopsis.Length > 0) scene.Synopsis = imported.Synopsis;
                if (imported.Notes.Length > 0) scene.Notes = imported.Notes;
                if (imported.Status.Length > 0) scene.Stage = StageKeyFor(book, imported.Status);
                if (imported.Label.Length > 0) scene.LabelKey = LabelKeyFor(book, imported.Label);
                // Scrivener's "include in compile" is exactly this question.
                scene.ExcludeFromExport = !imported.IncludeInCompile;

                foreach (var (fieldId, value) in imported.CustomFields)
                {
                    if (!fieldKeys.TryGetValue(fieldId, out var key)) continue;
                    scene.Properties ??= [];
                    scene.Properties[key] = value;
                }

                words += WordsIn(imported.Text);
                scenes++;
            }
        }

        var (characters, locations) = await ImportEntitiesAsync(project);
        var research = await ImportResearchAsync(project);

        await _workspace.Projects.SaveScenesAsync();
        await _workspace.Projects.SaveProjectAsync();
        return new ImportResultDto(chapters, scenes, words, characters, locations, research);
    }

    /// <summary>
    /// Character and setting sketches as Codex entries. The sketch prose lands
    /// in a section rather than being flattened into a description, because a
    /// filled-in Scrivener sheet is already a set of headed answers.
    /// </summary>
    private async Task<(int Characters, int Locations)> ImportEntitiesAsync(ScrivenerProject project)
    {
        if (project.Entities.Count == 0) return (0, 0);

        var entities = new EntityService(_workspace.Projects);
        var characters = 0;
        var locations = 0;

        foreach (var imported in project.Entities)
        {
            var sections = new List<EntitySection>();
            if (imported.Text.Length > 0)
                sections.Add(new EntitySection { Title = "Sketch", Content = imported.Text });
            if (imported.Notes.Length > 0)
                sections.Add(new EntitySection { Title = "Notes", Content = imported.Notes });

            if (imported.Kind == ScrivenerEntityKind.Character)
            {
                await entities.SaveCharacterAsync(new CharacterData
                {
                    Name = imported.Name,
                    Sections = sections
                });
                characters++;
            }
            else
            {
                await entities.SaveLocationAsync(new LocationData
                {
                    Name = imported.Name,
                    Sections = sections
                });
                locations++;
            }
        }

        return (characters, locations);
    }

    /// <summary>
    /// Everything outside the draft that carried content. Notes keep their
    /// prose; PDFs and pictures are copied into the project so it stays
    /// portable, exactly as a file dropped on the Research view would be.
    /// </summary>
    private async Task<int> ImportResearchAsync(ScrivenerProject project)
    {
        if (project.Research.Count == 0) return 0;

        var service = new ResearchService(_workspace.Projects, _workspace.FileService);
        var count = 0;

        foreach (var imported in project.Research)
        {
            var item = new ResearchItem
            {
                Title = imported.Title,
                Tags = imported.FolderTag.Length > 0 ? [imported.FolderTag] : []
            };

            if (imported.Kind == ScrivenerResearchKind.Note)
            {
                item.Type = ResearchItemType.Note;
                item.Content = imported.Text;
            }
            else
            {
                item.Type = imported.Kind switch
                {
                    ScrivenerResearchKind.Pdf => ResearchItemType.Pdf,
                    ScrivenerResearchKind.Image => ResearchItemType.Image,
                    _ => TypeFromExtension(imported.SourcePath)
                };
                item.Content = await service.ImportFileAsync(imported.SourcePath);
            }

            await service.SaveAsync(item);
            count++;
        }

        return count;
    }

    /// <summary>
    /// What an imported file is, for the kinds Scrivener does not distinguish
    /// itself. A recording the writer wrote a scene to should be playable in
    /// the Research view rather than sitting there as an anonymous file.
    /// </summary>
    private static ResearchItemType TypeFromExtension(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp3" or ".m4a" or ".wav" or ".aac" or ".flac" or ".ogg" => ResearchItemType.Audio,
            ".mp4" or ".mov" or ".m4v" or ".webm" or ".avi" or ".mkv" => ResearchItemType.Video,
            _ => ResearchItemType.File
        };

    /// <summary>
    /// Adds a scene-scoped manuscript property for each of the project's custom
    /// metadata fields, and returns the Scrivener field id to Novalist key map
    /// the scenes are written through.
    ///
    /// Custom metadata is as close as Scrivener gets to a field of the writer's
    /// own - a tension rating, a POV name, a draft flag. Dropping it forced
    /// whoever relied on it back onto the synopsis card.
    /// </summary>
    private static Dictionary<string, string> DeclareCustomFields(
        BookData book, ScrivenerProject project)
    {
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var field in project.CustomFields)
        {
            var existing = book.ManuscriptProperties.FirstOrDefault(p =>
                p.Scope == ManuscriptPropertyScope.Scene
                && string.Equals(p.Label, field.Title, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                keys[field.Id] = existing.Key;
                continue;
            }

            var definition = new ManuscriptPropertyDefinition
            {
                Key = KeyFrom(field.Title),
                Label = field.Title,
                Scope = ManuscriptPropertyScope.Scene,
                // A Scrivener list field is a closed vocabulary, which is
                // exactly what an enum property is; everything else is text.
                Type = field.Options.Count > 0 ? CustomPropertyType.Enum : CustomPropertyType.String,
                EnumOptions = field.Options.Count > 0 ? [.. field.Options] : null
            };
            book.ManuscriptProperties.Add(definition);
            keys[field.Id] = definition.Key;
        }

        return keys;
    }

    /// <summary>
    /// The book's stage for a Scrivener status, adding it when the book has no
    /// stage by that name. A writer whose statuses are "Zero draft" and "Beta"
    /// gets those, not the nearest of Novalist's five.
    /// </summary>
    private static string StageKeyFor(BookData book, string status)
    {
        var existing = book.SceneStages
            .FirstOrDefault(s => string.Equals(s.Label, status, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing.Key;

        var stage = new SceneStage { Key = KeyFrom(status), Label = status };
        book.SceneStages.Add(stage);
        return stage.Key;
    }

    /// <summary>The book's label for a Scrivener label, adding it when absent.</summary>
    private static string LabelKeyFor(BookData book, string label)
    {
        var existing = book.SceneLabels
            .FirstOrDefault(l => string.Equals(l.Label, label, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing.Key;

        var created = new SceneLabel { Key = KeyFrom(label), Label = label };
        book.SceneLabels.Add(created);
        return created.Key;
    }

    /// <summary>A stable key from a display name.</summary>
    private static string KeyFrom(string name)
    {
        var key = new string([.. name.ToLowerInvariant().Where(char.IsLetterOrDigit)]);
        return key.Length > 0 ? key : Guid.NewGuid().ToString("N")[..8];
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
                    string.Empty,
                    c.Scenes.Select(s => new ImportSceneDto(s.Title, s.WordCount)).ToArray()))
                .ToArray(),
            [], 0, 0, 0, 0);
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
    string[] Losses, int PartCount, int CharacterCount, int LocationCount, int ResearchCount);

public sealed record ImportResultDto(
    int Chapters, int Scenes, int Words, int Characters, int Locations, int Research);
