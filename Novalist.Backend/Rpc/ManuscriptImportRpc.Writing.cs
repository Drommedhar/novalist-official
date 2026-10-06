using System.Diagnostics;
using Novalist.Backend.Extensions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class ManuscriptImportRpc
{
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
    private async Task<ImportResultDto> RunScrivenerAsync(
        ScrivenerProject project,
        Action<string> setStage)
    {
        if (project.IsEmpty) return new ImportResultDto(0, 0, 0, 0, 0, 0);

        setStage("prepare-scrivener");
        var projects = _workspace.Projects;
        // Where to come back to. A folder sent to a draft or a book of its own is
        // filled by going there and returning, because chapters are only ever
        // created in whatever is active - so leaving the writer somewhere they
        // did not ask to be is the one thing this must not do.
        var homeBook = projects.ActiveBook ?? throw new InvalidOperationException("No active book.");
        var homeBookId = homeBook.Id;
        var homeDraftId = homeBook.ActiveDraftId;

        var chapters = 0;
        var scenes = 0;
        var words = 0;
        var draftsCreated = 0;
        var booksCreated = 0;

        foreach (var target in GroupTargets(project))
        {
            setStage($"write-{target.Kind.ToString().ToLowerInvariant()}");
            switch (target.Kind)
            {
                case ScrivenerTargetKind.Draft:
                    var draft = await projects.CreateDraftAsync(NameFor(target.Title, "Draft"));
                    await projects.SwitchDraftAsync(draft.Id);
                    draftsCreated++;
                    break;

                case ScrivenerTargetKind.Book:
                    var created = await projects.CreateBookAsync(NameFor(target.Title, "Book"));
                    await projects.SwitchBookAsync(created.Id);
                    booksCreated++;
                    break;
            }

            var filled = await FillAsync(target, project);
            chapters += filled.Chapters;
            scenes += filled.Scenes;
            words += filled.Words;

            // Back where the writer was, before the next target moves again.
            if (target.Kind == ScrivenerTargetKind.Draft)
            {
                await projects.SaveScenesAsync();
                await projects.SwitchDraftAsync(homeDraftId);
            }
            else if (target.Kind == ScrivenerTargetKind.Book)
            {
                await projects.SaveScenesAsync();
                await projects.SwitchBookAsync(homeBookId);
            }
        }

        // The Codex is the active book's and research is the project's, so both
        // land where the writer was rather than in whatever was created.
        setStage("write-entities");
        var (characters, locations) = await ImportEntitiesAsync(project);
        setStage("write-research");
        var research = await ImportResearchAsync(project);

        setStage("save-project");
        await projects.SaveScenesAsync();
        await projects.SaveProjectAsync();
        return new ImportResultDto(
            chapters, scenes, words, characters, locations, research, draftsCreated, booksCreated);
    }

    /// <summary>
    /// Creates one target's chapters and scenes in whatever book and draft is
    /// active, with the per-document metadata Novalist has a home for: the
    /// synopsis card, the document notes, the status as a scene stage, the label
    /// as a scene label, and "include in compile" as whether the scene exports.
    ///
    /// Stages, labels and custom fields are the book's, so they are resolved
    /// against the active book each time - a new book gets its own rather than
    /// silently sharing the one being imported from.
    /// </summary>
    private async Task<(int Chapters, int Scenes, int Words)> FillAsync(
        ScrivenerTargetGroup target, ScrivenerProject project)
    {
        var book = _workspace.Projects.ActiveBook ?? throw new InvalidOperationException("No active book.");
        var fieldKeys = DeclareCustomFields(book, project);
        var chapters = 0;
        var scenes = 0;
        var words = 0;

        foreach (var group in target.Chapters)
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
                await _workspace.WriteSceneAsync(
                    chapter.Guid, scene.Id, imported.Html, imported.Text);

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

        return (chapters, scenes, words);
    }

    /// <summary>A name for a created draft or book. A binder folder can be
    /// untitled, and "Draft" beats a row with no name on it at all.</summary>
    private static string NameFor(string title, string fallback)
        => title.Trim().Length > 0 ? title.Trim() : fallback;

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
                sections.Add(new EntitySection { Title = "Sketch", Content = imported.MarkdownText });
            if (imported.Notes.Length > 0)
                sections.Add(new EntitySection { Title = "Notes", Content = imported.MarkdownNotes });

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
                item.Content = imported.MarkdownText;
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

    /// <summary>Paragraph text without the tags, for the word count the
    /// manifest stores.</summary>
    private static string PlainTextOf(string html) =>
        Novalist.Core.Utilities.TextDiff.StripHtml(html);
}
