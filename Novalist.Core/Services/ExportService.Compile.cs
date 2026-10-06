using System.Text;
using Novalist.Core.Models;
using Novalist.Core.Utilities;
using static Novalist.Core.Services.ExportProse;

namespace Novalist.Core.Services;

public partial class ExportService
{
    public async Task<List<ChapterExportContent>> CompileChaptersAsync(ExportOptions options)
    {
        var chapters = await CompileChaptersAsync(options, ActiveSource());

        // A box set: the open book first, then each further volume the writer
        // asked for, each announced by a heading of its own so the contents
        // nests rather than running eighty chapters together.
        var volumes = await OtherVolumesAsync(options);
        if (volumes.Count == 0) return chapters;

        foreach (var (book, volume) in volumes)
        {
            chapters.Add(new ChapterExportContent
            {
                Title = book.Name,
                // Its own heading and no scenes: a divider announcing the
                // volume, never numbered, because it is not a chapter of one.
                Heading = book.Name,
                IsVolume = true,
                // An anthology's volumes are by different people. Without this
                // a collection of six writers goes out under one name.
                Subtitle = AuthorLine(book, options),
                Scenes = []
            });
            chapters.AddRange(await CompileChaptersAsync(options, volume));
        }

        // Each book numbers its chapters from its own start, so appending one
        // to another leaves two chapters claiming the same position - and any
        // writer that sorts by it would interleave the volumes. Renumbered once
        // across the whole set, and only when there is more than one book in
        // it, so a single-book export is untouched.
        for (var i = 0; i < chapters.Count; i++) chapters[i].Order = i;

        return chapters;
    }

    /// <summary>
    /// The further books this export was asked for, in the project's own order,
    /// never including the one already compiled.
    /// </summary>
    private async Task<List<(BookData Book, VolumeSource Source)>> OtherVolumesAsync(ExportOptions options)
    {
        var wanted = options.IncludedBookIds;
        if (wanted == null || wanted.Count == 0) return [];

        var sources = new List<(BookData, VolumeSource)>();
        foreach (var book in _projectService.CurrentProject?.Books ?? [])
        {
            if (book.Id == _projectService.ActiveBook?.Id) continue;
            if (!wanted.Contains(book.Id)) continue;

            var manifest = await _projectService.LoadScenesManifestForAsync(book);
            if (manifest == null) continue;

            sources.Add((book, new VolumeSource(
                book,
                [.. book.Chapters.OrderBy(c => c.Order)],
                guid => manifest.Chapters.TryGetValue(guid, out var scenes)
                    ? [.. scenes.Where(s => s.ArchivedAt == null).OrderBy(s => s.Order)]
                    : [],
                (chapter, scene) => _projectService.ReadSceneContentForAsync(book, chapter, scene),
                SelectAll: true)));
        }
        return sources;
    }

    private async Task<List<ChapterExportContent>> CompileChaptersAsync(
        ExportOptions options, VolumeSource source)
    {
        var chapters = source.Chapters
            .Where(c => source.SelectAll || options.SelectedChapterGuids.Contains(c.Guid))
            .OrderBy(c => c.Order)
            .ToList();

        var tokens = PrepareCompileContext(options, source, chapters);

        var result = new List<ChapterExportContent>();
        var preset = options.ResolvePreset();
        // Counts only the sections that carry a number.
        var numbered = 0;

        foreach (var chapter in chapters)
        {
            // A prologue is not Chapter One, and the chapter after it is. The
            // count walks past the sections standing outside it rather than
            // being the position in the list. Resolved before the scenes are
            // compiled, so a placeholder in the prose reads the same number
            // the heading does.
            var sectionType = SectionTypes.Resolve(
                chapter.SectionTypeKey, source.Book?.SectionTypes);
            if (sectionType.Numbered) numbered++;

            var scenes = source.ScenesFor(chapter.Guid)
                // Three ways a scene stays out of the book: it is not in the
                // book at all, the writer held it back from exports, or it is
                // not at a stage this export asked for.
                .Where(s => !s.Inactive)
                .Where(s => !s.ExcludeFromExport)
                .Where(s => options.IncludedStages == null
                    || options.IncludedStages.Count == 0
                    || options.IncludedStages.Contains(s.Stage ?? string.Empty))
                .ToList();
            var sceneContents = new List<SceneExportContent>();

            await CompileSceneContentsAsync(options, source, chapter, scenes, sceneContents, tokens with { ChapterNumber = numbered });

            var chapterTokens = tokens with
            {
                ChapterNumber = numbered,
                ChapterTitle = chapter.Title,
                Act = chapter.Act ?? string.Empty
            };
            var title = ExportTokens.Resolve(chapter.Title, chapterTokens);
            result.Add(new ChapterExportContent
            {
                Title = title,
                Order = chapter.Order,
                Guid = chapter.Guid,
                Subtitle = ExportTokens.Resolve(chapter.Subtitle ?? string.Empty, chapterTokens),
                // Built here so every writer prints the same heading and a
                // placeholder in a heading format resolves in all of them.
                Heading = ExportTokens.Resolve(
                    preset.ChapterHeading(numbered, title, sectionType), chapterTokens),
                HideHeading = chapter.HideHeading,
                Scenes = sceneContents
            });
        }

        // The layout's own lines, resolved once against the finished book.
        options.ResolvedSeparator = ExportTokens.Resolve(preset.SceneSeparator, tokens);
        options.ResolvedRunningHead = ExportTokens.Resolve(preset.RunningHead, tokens);

        return result;
    }

    /// <summary>
    /// Heading a matter page should print. An explicit title always wins. With
    /// none, kinds that conventionally carry a heading get their kind name and
    /// the rest get none - a dedication with the word "Dedication" over it is
    /// not how books are set.
    /// </summary>
    internal static string ResolveMatterTitle(BookMatterElement element)
    {
        if (!string.IsNullOrWhiteSpace(element.Title))
            return element.Title.Trim();

        return BookMatterElement.ShowsHeadingByDefault(element.Kind)
            ? SpaceCamelCase(element.Kind.ToString())
            : string.Empty;
    }

    /// <summary>"AboutTheAuthor" to "About The Author", for a default heading.</summary>
    internal static string SpaceCamelCase(string value)
    {
        var builder = new StringBuilder(value.Length + 4);
        for (var i = 0; i < value.Length; i++)
        {
            if (i > 0 && char.IsUpper(value[i]))
                builder.Append(' ');
            builder.Append(value[i]);
        }
        return builder.ToString();
    }

    private TokenContext PrepareCompileContext(ExportOptions options, VolumeSource source, List<ChapterData> chapters)
    {
        // Publishing metadata belongs to the book, so the caller never has to
        // assemble it - every export path gets it by opening the project.
        options.Publishing = _projectService.ActiveBook?.Publishing ?? new Models.PublishingMetadata();
        options.CustomPresets = [.. _projectService.ActiveBook?.ExportPresets ?? []];

        // Matter pages come from the book, not the chapter selection: they frame
        // the whole book rather than belonging to any chapter.
        options.Replacements = [.. _projectService.ActiveBook?.ExportReplacements ?? []];

        // A title page that says "Book two of the Salt Road" had to be typed
        // out and remembered; a token resolves it from the book every time.
        var store = options.ResolveRetailer();
        var tokens = new TokenContext
        {
            Title = options.Title,
            Author = options.Author,
            Isbn = options.Publishing.NormalizedIsbn() ?? string.Empty,
            Publisher = options.Publishing.Publisher,
            Series = options.Publishing.SeriesName,
            SeriesIndex = options.Publishing.SeriesPosition,
            // The store this build is for, so back matter can point a reader at
            // the shop they bought it in rather than at a competitor.
            StoreName = store?.Name ?? string.Empty,
            StoreLink = store?.Url ?? string.Empty,
            // Both were in the token table and in the manual and neither was
            // ever set, so a title page saying "<$wordcount> words" printed a
            // zero at whoever it was sent to.
            WordCount = chapters
                // The pass's own book, so a volume counts its own words rather
                // than the open book's.
                .SelectMany(c => source.ScenesFor(c.Guid))
                .Where(sc => !sc.Inactive && !sc.ExcludeFromExport)
                .Sum(sc => sc.WordCount),
            PageCount = 0
        };
        // Normseiten when the layout is on that grid, and the usual 250 words
        // to a page otherwise. An estimate either way, and saying so.
        tokens = tokens with
        {
            PageCount = (int)Math.Ceiling(tokens.WordCount / 250.0)
        };

        options.Matter = (_projectService.ActiveBook?.Matter ?? [])
            .Where(m => m.Included && !string.IsNullOrWhiteSpace(m.Content))
            .OrderBy(m => m.Placement)
            .ThenBy(m => m.Order)
            .Select(m => new MatterExportContent
            {
                Id = m.Id,
                Kind = m.Kind.ToString(),
                Placement = m.Placement.ToString(),
                Title = ExportTokens.Resolve(ResolveMatterTitle(m), tokens),
                HtmlContent = Models.ExportReplacements.Apply(
                    ExportTokens.Resolve(m.Content, tokens), options.Replacements),
                Order = m.Order,
                InTableOfContents = m.InTableOfContents
            })
            .ToList();

        return tokens;
    }

    private async Task CompileSceneContentsAsync(ExportOptions options, VolumeSource source, ChapterData chapter, List<SceneData> scenes, List<SceneExportContent> sceneContents, TokenContext tokens)
    {
        foreach (var scene in scenes)
        {
            // Suggested edits resolve here, once, so no writer downstream
            // has to know they exist. An export is a finished book: an
            // insertion nobody rejected is in it, a deletion nobody
            // accepted is not, and the markup itself never reaches a page.
            // Suggested edits resolve, then the book's compile-time rules
            // run - on the way out only. Replace All writes to the source
            // scenes; these never do, which is what makes "the submission
            // copy spells it out and the ebook uses the glyph" possible
            // without keeping two drafts.
            var html = Models.ExportReplacements.Apply(
                TrackedChanges.Final(ResolveImagePaths(
                    await source.ReadScene(chapter, scene))),
                options.Replacements);
            // The chapter and scene the writer is in, so <$chaptertitle>
            // in the prose means this chapter rather than nothing. These
            // were documented, in the token table, and populated nowhere.
            var sceneTokens = tokens with
            {
                ChapterNumber = tokens.ChapterNumber,
                ChapterTitle = chapter.Title,
                SceneTitle = scene.Title,
                Act = chapter.Act ?? string.Empty
            };
            html = ExportTokens.Resolve(html, sceneTokens);
            sceneContents.Add(new SceneExportContent
            {
                Title = ExportTokens.Resolve(scene.Title, sceneTokens),
                Order = scene.Order,
                HtmlContent = html,
                Id = scene.Id,
                // Ids are lowercased because the inline parser lowercases
                // the tag it finds them in.
                Footnotes = (scene.Footnotes ?? [])
                    .Where(n => !string.IsNullOrWhiteSpace(n.Text))
                    .GroupBy(n => n.Id.ToLowerInvariant())
                    .ToDictionary(g => g.Key, g => g.First().Text),
                Comments = (scene.Comments ?? [])
                    .Where(c => !c.Resolved && !string.IsNullOrWhiteSpace(c.Text))
                    .Select(c => new SceneExportComment
                    {
                        Id = c.Id,
                        AnchorText = c.AnchorText ?? string.Empty,
                        Text = c.Text,
                        CreatedAt = c.CreatedAt
                    })
                    .ToList()
            });
        }

    }
}
