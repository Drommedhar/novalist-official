using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// The whole project in one document. Plot threads, research, saved lists and
/// collections had no export path at all, so a project could be read out in
/// pieces and never as a whole.
/// </summary>
public class WorldArchiveTests
{
    private static MetadataExport Metadata() => new()
    {
        Title = "Salt Road",
        Author = "Ada",
        Scenes = [new SceneMetadataRow { Chapter = "One", Scene = "Arrival", Words = 900,
            Synopsis = "She gets off the train." }],
        Codex = [new EntityMetadataRow
        {
            Kind = "Character",
            Name = "Mira",
            Properties = { ["Role"] = "Protagonist" },
            Sections = { ["History"] = "Born north." },
            Relationships = { "sister: Tomas" }
        }]
    };

    private static (ProjectMetadata Project, BookData Book) Project()
    {
        var book = new BookData { Name = "Salt Road" };
        book.Plotlines.Add(new PlotlineData
        {
            Name = "The debt",
            Importance = PlotlineImportance.Main,
            Description = "Who owes whom.",
            Steps =
            [
                new PlotlineStep { Text = "It is called in", Order = 0, Resolved = true },
                new PlotlineStep { Text = "She pays", Order = 1 }
            ]
        });
        book.Collections.Add(new SceneCollection { Name = "To revise", SceneIds = ["a", "b"] });
        book.Maps.Add(new MapReference { Name = "The valley" });

        var project = new ProjectMetadata { Name = "SaltProject" };
        project.ResearchItems.Add(new ResearchItem
        {
            Title = "Rail timetables",
            Content = "The 6.15 does not run on Sundays.",
            Tags = ["trains"]
        });
        project.SmartLists.Add(new SmartList
        {
            Name = "Mira's scenes",
            Rules = [new SmartListRule { Field = "pov", Op = SmartListOperator.Contains, Value = "Mira" }]
        });
        return (project, book);
    }

    [Fact]
    public void EverythingTheProjectHoldsIsInTheDocument()
    {
        var (project, book) = Project();

        var archive = WorldArchive.Build(Metadata(), project, book);

        Assert.Equal("SaltProject", archive.Project);
        Assert.Equal("Salt Road", archive.Book);
        Assert.Equal("Ada", archive.Author);
        Assert.Single(archive.Scenes);
        Assert.Single(archive.Codex);
        Assert.Equal("The debt", Assert.Single(archive.Plotlines).Name);
        Assert.Equal("Rail timetables", Assert.Single(archive.Research).Title);
        Assert.Equal("Mira's scenes", Assert.Single(archive.SmartLists).Name);
        Assert.Equal("To revise", Assert.Single(archive.Collections).Name);
        Assert.Equal("The valley", Assert.Single(archive.Maps));
    }

    [Fact]
    public void AResolvedStepIsMarkedRatherThanDropped()
    {
        var (project, book) = Project();

        var steps = WorldArchive.Build(Metadata(), project, book).Plotlines.Single().Steps;

        Assert.Equal("[done] It is called in", steps[0]);
        Assert.Equal("She pays", steps[1]);
        Assert.Equal(1, WorldArchive.Build(Metadata(), project, book).Plotlines.Single().Unresolved);
    }

    [Fact]
    public void ARuleReadsAsASentence()
    {
        var (project, book) = Project();

        // A blob of field/op/value says less to a reader than "pov Contains Mira".
        Assert.Equal("pov Contains Mira",
            WorldArchive.Build(Metadata(), project, book).SmartLists.Single().Rules.Single());
    }

    [Fact]
    public void AnEmptyProjectStillProducesADocument()
    {
        var archive = WorldArchive.Build(new MetadataExport(), null, null);

        Assert.Empty(archive.Plotlines);
        Assert.Empty(archive.Research);
        Assert.Contains("\"scenes\": []", WorldArchive.Json(archive));
    }

    [Fact]
    public void TheJsonKeepsAccentsReadable()
    {
        var archive = WorldArchive.Build(
            new MetadataExport { Title = "Für Elise" }, null, null);

        Assert.Contains("Für Elise", WorldArchive.Json(archive));
    }

    // ── The page ──

    [Fact]
    public void ThePageCarriesEverySection()
    {
        var (project, book) = Project();

        var html = WorldArchive.Html(WorldArchive.Build(Metadata(), project, book));

        Assert.StartsWith("<!doctype html>", html);
        foreach (var heading in new[]
                 { "Scenes", "Codex", "Plot threads", "Research", "Saved lists", "Collections", "Maps" })
        {
            Assert.Contains($">{heading} <span", html);
        }
        Assert.Contains("Arrival", html);
        Assert.Contains("Born north.", html);
        Assert.Contains("sister: Tomas", html);
        Assert.Contains("The 6.15 does not run on Sundays.", html);
        Assert.Contains("The valley", html);
    }

    [Fact]
    public void AnEmptySectionSaysSoRatherThanVanishing()
    {
        // "No research" and "research was not exported" are different things,
        // and a reader cannot tell them apart from a gap.
        var html = WorldArchive.Html(WorldArchive.Build(new MetadataExport(), null, null));

        Assert.Contains("Nothing here.", html);
        Assert.Contains(">Research <span class=\"kind\">(0)</span>", html);
    }

    [Fact]
    public void ProseThatWouldBreakThePageIsEscaped()
    {
        var metadata = new MetadataExport
        {
            Title = "Tom & Jerry",
            Scenes = [new SceneMetadataRow { Chapter = "<Act>", Scene = "A", Synopsis = "a > b & c" }]
        };

        var html = WorldArchive.Html(WorldArchive.Build(metadata, null, null));

        Assert.Contains("Tom &amp; Jerry", html);
        Assert.Contains("&lt;Act&gt;", html);
        Assert.Contains("a &gt; b &amp; c", html);
        Assert.DoesNotContain("<Act>", html);
    }

    [Fact]
    public void ThePageIsOneFileWithNothingToFetch()
    {
        var html = WorldArchive.Html(WorldArchive.Build(Metadata(), null, null));

        // It has to open by double-clicking and survive being emailed, so it
        // cannot arrive with its stylesheet missing.
        Assert.Contains("<style>", html);
        Assert.DoesNotContain("<link", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("http://", html);
    }

    // A document named for the project that carried one book of a trilogy was
    // two-thirds missing and said nothing about the fact.
    [Fact]
    public void TheProjectsOtherBooksAreCarriedToo()
    {
        var (project, book) = Project();
        var archive = WorldArchive.Build(Metadata(), project, book);
        var second = new BookData { Name = "Book Two" };
        second.Plotlines.Add(new PlotlineData { Name = "The reckoning" });
        second.Collections.Add(new SceneCollection { Name = "Act one", SceneIds = ["x"] });
        second.Maps.Add(new MapReference { Name = "The north" });

        WorldArchive.AddVolume(archive, second,
            [new SceneMetadataRow { Chapter = "Later", Scene = "Elsewhere", Words = 400 }]);

        var volume = Assert.Single(archive.OtherBooks);
        Assert.Equal("Book Two", volume.Book);
        Assert.Equal("Elsewhere", Assert.Single(volume.Scenes).Scene);
        Assert.Equal("The reckoning", Assert.Single(volume.Plotlines).Name);
        Assert.Equal("Act one", Assert.Single(volume.Collections).Name);
        Assert.Equal("The north", Assert.Single(volume.Maps));

        // The open book's own lists are untouched by the addition.
        Assert.Equal("The debt", Assert.Single(archive.Plotlines).Name);
    }

    [Fact]
    public void ThePageListsTheOtherBooksAndTheirOutlines()
    {
        var (project, book) = Project();
        var archive = WorldArchive.Build(Metadata(), project, book);
        WorldArchive.AddVolume(archive, new BookData { Name = "Book Two" },
            [new SceneMetadataRow { Chapter = "Later", Scene = "Elsewhere", Synopsis = "It ends." }]);

        var html = WorldArchive.Html(archive);

        Assert.Contains(">Other books <span", html);
        Assert.Contains("Book Two", html);
        Assert.Contains("It ends.", html);
    }

    [Fact]
    public void ASingleBookProjectSaysThereAreNoOthers()
    {
        var (project, book) = Project();

        var html = WorldArchive.Html(WorldArchive.Build(Metadata(), project, book));

        // Printed with a count of zero rather than left out, like every other
        // section: a gap cannot tell you which.
        Assert.Contains(">Other books <span class=\"kind\">(0)</span>", html);
    }
}
