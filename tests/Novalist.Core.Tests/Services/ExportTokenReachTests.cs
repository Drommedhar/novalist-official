using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Where a placeholder actually resolves.
///
/// The token language existed, the tokens were listed in the manual, and the
/// only thing that ever resolved one was a matter page. Six of them -
/// chapter number, chapter numeral, chapter title, scene title, act, and both
/// counts - were populated by nothing at all, so a title page reading
/// "&lt;$wordcount&gt; words" printed a zero at whoever it was sent to.
/// </summary>
public class ExportTokenReachTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly IProjectService _project = Substitute.For<IProjectService>();
    private readonly ChapterData _first = new() { Title = "The Fall", Order = 1, Act = "Act One" };
    private readonly ChapterData _second = new() { Title = "The Rise", Order = 2 };

    public void Dispose() => _dir.Dispose();

    /// <summary>Two chapters, so a number that is always one cannot pass.</summary>
    private ExportOptions Setup(string firstProse, string secondProse = "<p>Ordinary.</p>",
        ExportPreset? layout = null)
    {
        var one = new SceneData { Title = "Arrival", Order = 1, ChapterGuid = _first.Guid, WordCount = 300 };
        var two = new SceneData { Title = "Departure", Order = 1, ChapterGuid = _second.Guid, WordCount = 200 };
        _project.GetChaptersOrdered().Returns([_first, _second]);
        _project.GetScenesForChapter(_first.Guid).Returns([one]);
        _project.GetScenesForChapter(_second.Guid).Returns([two]);
        _project.ReadSceneContentAsync(_first, one).Returns(firstProse);
        _project.ReadSceneContentAsync(_second, two).Returns(secondProse);
        // A layout the writer authored lives on the book; the compile reloads
        // the list from there, so handing it to the options alone proves
        // nothing about the path a real export takes.
        _project.ActiveBook.Returns(new BookData
        {
            ExportPresets = layout == null ? [] : [layout]
        });

        return new ExportOptions
        {
            Format = ExportFormat.Markdown,
            Title = "Salt Road",
            Author = "D. G.",
            PresetId = layout?.Id,
            SelectedChapterGuids = [_first.Guid, _second.Guid]
        };
    }

    private ExportService Service() => new(_project);

    /// <summary>A layout of the writer's own, built from the default.</summary>
    private static ExportPreset Layout(Func<ExportPreset, ExportPreset> shape)
        => shape(ExportPresets.GetById(ExportPresets.DefaultId) with
        {
            Id = "custom",
            IsCustom = true
        });

    [Fact]
    public async Task APlaceholderInTheProseResolves()
    {
        var options = Setup("<p>From <$chaptertitle>, in <$act>.</p>");

        var chapters = await Service().CompileChaptersAsync(options);

        // The token table listed these and nothing populated them, so a
        // placeholder in the prose came out as an empty string.
        Assert.Contains("From The Fall, in Act One.", chapters[0].Scenes[0].HtmlContent);
    }

    [Fact]
    public async Task TheChapterNumberIsTheChaptersOwn()
    {
        var options = Setup("<p><$chapternumber> / <$chapterroman></p>", "<p><$chapternumber> / <$chapterroman></p>");

        var chapters = await Service().CompileChaptersAsync(options);

        Assert.Contains("1 / I", chapters[0].Scenes[0].HtmlContent);
        Assert.Contains("2 / II", chapters[1].Scenes[0].HtmlContent);
    }

    [Fact]
    public async Task TheSceneTitleResolvesInsideItsOwnScene()
    {
        var options = Setup("<p>This is <$scenetitle>.</p>");

        var chapters = await Service().CompileChaptersAsync(options);

        Assert.Contains("This is Arrival.", chapters[0].Scenes[0].HtmlContent);
    }

    [Fact]
    public async Task TheWordCountIsTheBooksOwn()
    {
        var options = Setup("<p><$wordcount> words, about <$pagecount> pages.</p>");

        var chapters = await Service().CompileChaptersAsync(options);

        // 500 words across the two scenes, and the usual 250 to a page.
        Assert.Contains("500 words, about 2 pages.", chapters[0].Scenes[0].HtmlContent);
    }

    [Fact]
    public async Task ASceneKeptOutOfTheBookIsNotCounted()
    {
        var held = new SceneData
        {
            Title = "Held back",
            Order = 2,
            ChapterGuid = _first.Guid,
            WordCount = 9000,
            ExcludeFromExport = true
        };
        var one = new SceneData { Title = "Arrival", Order = 1, ChapterGuid = _first.Guid, WordCount = 300 };
        _project.GetChaptersOrdered().Returns([_first]);
        _project.GetScenesForChapter(_first.Guid).Returns([one, held]);
        _project.ReadSceneContentAsync(_first, one).Returns("<p><$wordcount></p>");
        _project.ActiveBook.Returns(new BookData());
        var options = new ExportOptions
        {
            Format = ExportFormat.Markdown,
            Title = "Salt Road",
            SelectedChapterGuids = [_first.Guid]
        };

        var chapters = await Service().CompileChaptersAsync(options);

        // A count that includes what the book does not is a count nobody can
        // put on a query letter.
        Assert.Contains("300", chapters[0].Scenes[0].HtmlContent);
    }

    [Fact]
    public async Task ATokenNobodyKnowsIsLeftAsWritten()
    {
        var options = Setup("<p>Written by <$autor>.</p>");

        var chapters = await Service().CompileChaptersAsync(options);

        // Silently deleting something a writer typed is worse than printing
        // it, and it makes the typo visible instead of invisible.
        Assert.Contains("<$autor>", chapters[0].Scenes[0].HtmlContent);
    }

    // ─── Headings ────────────────────────────────────────────────────

    [Fact]
    public async Task TheHeadingIsBuiltOnceForEveryWriter()
    {
        var options = Setup("<p>Prose.</p>", layout: Layout(f => f with
        {
            ChapterTitleFormat = "Chapter {number}: {title}"
        }));

        var chapters = await Service().CompileChaptersAsync(options);

        // Six writers used to each call the format themselves, so a resolver
        // wired into one of them was wired into none of the others.
        Assert.Equal("Chapter 1: The Fall", chapters[0].Heading);
        Assert.Equal("Chapter 2: The Rise", chapters[1].Heading);
    }

    [Fact]
    public async Task APlaceholderInTheHeadingFormatResolves()
    {
        var options = Setup("<p>Prose.</p>", layout: Layout(f => f with
        {
            ChapterTitleFormat = "<$title> - {title}"
        }));

        var chapters = await Service().CompileChaptersAsync(options);

        Assert.Equal("Salt Road - The Fall", chapters[0].Heading);
    }

    [Fact]
    public async Task APlaceholderInAChapterTitleResolves()
    {
        _first.Title = "Return to <$title>";
        var options = Setup("<p>Prose.</p>");

        var chapters = await Service().CompileChaptersAsync(options);

        Assert.Equal("Return to Salt Road", chapters[0].Title);
    }

    // ─── Separator and running head ──────────────────────────────────

    [Fact]
    public async Task TheSeparatorResolvesItsPlaceholders()
    {
        var options = Setup("<p>Prose.</p>", layout: Layout(f => f with
        {
            SceneSeparator = "<$title>"
        }));

        await Service().CompileChaptersAsync(options);

        Assert.Equal("Salt Road", options.ResolvedSeparator);
    }

    [Fact]
    public async Task ALayoutCanAuthorTheRunningHead()
    {
        var options = Setup("<p>Prose.</p>", layout: Layout(f => f with
        {
            RunningHead = "<$author> / <$title>"
        }));

        await Service().CompileChaptersAsync(options);

        Assert.Equal("D. G. / Salt Road", ExportService.RunningHead(options));
    }

    [Fact]
    public void SayingNothingKeepsTheSubmissionDefault()
    {
        var options = new ExportOptions { Title = "Salt Road", Author = "Dominik Goblirsch" };

        // What every manuscript export printed when this could not be authored
        // at all, so an existing layout comes out unchanged.
        Assert.Equal("Goblirsch / SALT ROAD", ExportService.RunningHead(options));
    }

    [Fact]
    public void ALongTitleIsCutRatherThanRunIntoThePageNumber()
    {
        var options = new ExportOptions
        {
            Title = "A Title Long Enough To Reach The Page Number",
            Author = "Vane"
        };

        Assert.Equal("Vane / A TITLE LONG ENOUGH TO REAC...", ExportService.RunningHead(options));
    }

    [Fact]
    public void ARunningHeadOfOnlySpacesIsNoRunningHead()
    {
        var options = new ExportOptions
        {
            Title = "Salt Road", Author = "Vane", ResolvedRunningHead = "   "
        };

        Assert.Equal("Vane / SALT ROAD", ExportService.RunningHead(options));
    }

    [Fact]
    public void ABookWithNoAuthorStillGetsAHead()
    {
        var options = new ExportOptions { Title = "Salt Road" };

        Assert.Equal(" / SALT ROAD", ExportService.RunningHead(options));
    }
}
