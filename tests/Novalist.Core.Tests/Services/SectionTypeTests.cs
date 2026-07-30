using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// What a chapter is, as opposed to what it is called.
///
/// Novalist had one ladder - book, draft, chapter, scene - so a prologue was a
/// chapter, which meant it was numbered as one, which meant the first real
/// chapter came out as Chapter Two. The only fix was hiding the heading and
/// typing "Prologue" into the prose, where no contents list could see it.
/// </summary>
public class SectionTypeTests
{
    private readonly IProjectService _project = Substitute.For<IProjectService>();

    private static ExportPreset Layout(Func<ExportPreset, ExportPreset> shape)
        => shape(ExportPresets.GetById(ExportPresets.DefaultId) with
        {
            Id = "custom",
            IsCustom = true,
            ChapterTitleFormat = "Chapter {number}: {title}"
        });

    /// <summary>Chapters in order, each with the type it was given.</summary>
    private ExportOptions Setup(ExportPreset? layout, params (string Title, string Type)[] chapters)
    {
        var made = chapters
            .Select((c, i) => new ChapterData { Title = c.Title, Order = i + 1, SectionTypeKey = c.Type })
            .ToList();

        _project.GetChaptersOrdered().Returns(made);
        foreach (var chapter in made)
        {
            var scene = new SceneData { Title = "S", Order = 1, ChapterGuid = chapter.Guid };
            _project.GetScenesForChapter(chapter.Guid).Returns([scene]);
            _project.ReadSceneContentAsync(chapter, scene).Returns("<p>Prose.</p>");
        }
        _project.ActiveBook.Returns(new BookData
        {
            ExportPresets = layout == null ? [] : [layout]
        });

        return new ExportOptions
        {
            Format = ExportFormat.Markdown,
            Title = "Salt Road",
            PresetId = layout?.Id,
            SelectedChapterGuids = [.. made.Select(c => c.Guid)]
        };
    }

    // ─── Resolving a type ────────────────────────────────────────────

    [Theory]
    [InlineData("prologue", false)]
    [InlineData("epilogue", false)]
    [InlineData("interlude", false)]
    [InlineData("part", false)]
    [InlineData("chapter", true)]
    public void TheBuiltInTypes(string key, bool numbered)
        => Assert.Equal(numbered, SectionTypes.Resolve(key, null).Numbered);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("a-type-somebody-deleted")]
    public void AnythingUnrecognisedIsAnOrdinaryChapter(string? key)
    {
        // A chapter that vanishes from the export because its type was deleted
        // is the worst outcome available here.
        Assert.Equal(SectionTypes.Chapter, SectionTypes.Resolve(key, null).Key);
    }

    [Fact]
    public void TheBooksOwnTypeWinsOverTheBuiltIn()
    {
        var own = new SectionType { Key = "prologue", Name = "Vorspiel", Numbered = true };

        var resolved = SectionTypes.Resolve("prologue", [own]);

        Assert.Equal("Vorspiel", resolved.Name);
        Assert.True(resolved.Numbered);
    }

    [Fact]
    public void APickerOffersTheBooksOwnThenTheBuiltIns()
    {
        var own = new SectionType { Key = "letter", Name = "Letter", Numbered = false };

        var all = SectionTypes.All([own]);

        Assert.Equal("letter", all[0].Key);
        Assert.Contains(all, t => t.Key == "chapter");
        // The book overriding a built-in gets one entry, not two.
        Assert.Equal(all.Select(t => t.Key), all.Select(t => t.Key).Distinct());
    }

    // ─── Numbering ───────────────────────────────────────────────────

    [Fact]
    public async Task APrologueIsNotChapterOne()
    {
        var options = Setup(Layout(p => p),
            ("Before", "prologue"), ("The Fall", "chapter"), ("The Rise", "chapter"));

        var chapters = await new ExportService(_project).CompileChaptersAsync(options);

        // The count walks past the sections standing outside it.
        Assert.Equal("Before", chapters[0].Heading);
        Assert.Equal("Chapter 1: The Fall", chapters[1].Heading);
        Assert.Equal("Chapter 2: The Rise", chapters[2].Heading);
    }

    [Fact]
    public async Task AnEpilogueDoesNotTakeTheNextNumberEither()
    {
        var options = Setup(Layout(p => p),
            ("The Fall", "chapter"), ("After", "epilogue"), ("The Rise", "chapter"));

        var chapters = await new ExportService(_project).CompileChaptersAsync(options);

        Assert.Equal("Chapter 1: The Fall", chapters[0].Heading);
        Assert.Equal("After", chapters[1].Heading);
        Assert.Equal("Chapter 2: The Rise", chapters[2].Heading);
    }

    [Fact]
    public async Task APlaceholderInTheProseReadsTheSameNumberAsTheHeading()
    {
        var options = Setup(Layout(p => p), ("Before", "prologue"), ("The Fall", "chapter"));
        var chapters = _project.GetChaptersOrdered();
        _project.ReadSceneContentAsync(chapters[1], Arg.Any<SceneData>())
            .Returns("<p>This is chapter <$chapternumber>.</p>");

        var compiled = await new ExportService(_project).CompileChaptersAsync(options);

        // The heading says Chapter 1; the prose must not say Chapter 2.
        Assert.Contains("This is chapter 1.", compiled[1].Scenes[0].HtmlContent);
    }

    [Fact]
    public async Task ABookOfOrdinaryChaptersIsUnchanged()
    {
        var options = Setup(Layout(p => p), ("The Fall", ""), ("The Rise", ""));

        var chapters = await new ExportService(_project).CompileChaptersAsync(options);

        // Every book that existed before types has no types on it.
        Assert.Equal("Chapter 1: The Fall", chapters[0].Heading);
        Assert.Equal("Chapter 2: The Rise", chapters[1].Heading);
    }

    // ─── A layout describing a type ──────────────────────────────────

    [Fact]
    public async Task ALayoutCanSayHowAPrologueIsSet()
    {
        var options = Setup(
            Layout(p => p with
            {
                SectionLayouts = [new SectionLayout { TypeKey = "prologue", TitleFormat = "Prologue - {title}" }]
            }),
            ("Before", "prologue"), ("The Fall", "chapter"));

        var chapters = await new ExportService(_project).CompileChaptersAsync(options);

        // One draft, three books: the layout decides what a prologue looks
        // like, not the chapter.
        Assert.Equal("Prologue - Before", chapters[0].Heading);
        Assert.Equal("Chapter 1: The Fall", chapters[1].Heading);
    }

    [Fact]
    public void ATypeNobodyDescribedIsSetTheWayChaptersAre()
    {
        var preset = Layout(p => p);

        var heading = preset.ChapterHeading(3, "The Rise", SectionTypes.Resolve("chapter", null));

        Assert.Equal("Chapter 3: The Rise", heading);
    }

    [Fact]
    public void AnUnnumberedTypeDropsTheNumberRatherThanPrintingABlank()
    {
        var preset = Layout(p => p);

        var heading = preset.ChapterHeading(1, "Before", SectionTypes.Resolve("prologue", null));

        // "Chapter : Before" is what a naive substitution produces.
        Assert.Equal("Before", heading);
    }

    [Fact]
    public void ALayoutCanOverrideNumeralsAndCapitalsPerType()
    {
        var preset = Layout(p => p with
        {
            SectionLayouts =
            [
                new SectionLayout
                {
                    TypeKey = "chapter",
                    TitleFormat = "{number}. {title}",
                    NumberStyle = ChapterNumberStyle.RomanUpper,
                    Uppercase = true
                }
            ]
        });

        var heading = preset.ChapterHeading(4, "The Rise", SectionTypes.Resolve("chapter", null));

        Assert.Equal("IV. THE RISE", heading);
    }

    [Fact]
    public void AnEmptyFormatFallsBackRatherThanPrintingNothing()
    {
        var preset = Layout(p => p with
        {
            ChapterTitleFormat = "  ",
            SectionLayouts = [new SectionLayout { TypeKey = "chapter", TitleFormat = "  " }]
        });

        Assert.Equal("The Rise",
            preset.ChapterHeading(1, "The Rise", SectionTypes.Resolve("chapter", null)));
    }
}
