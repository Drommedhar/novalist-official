using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Planning boards. A board sits outside the manuscript: nothing on it affects
/// chapters, scenes or counts until a card is explicitly promoted.
/// </summary>
public class CanvasServiceTests
{
    private const string DraftRoot = "/book/draft";

    private static (CanvasService Sut, IProjectService Project, InMemoryFileService Files, BookData Book)
        Build(bool withBook = true)
    {
        var project = Substitute.For<IProjectService>();
        var files = new InMemoryFileService();
        var book = new BookData();
        if (withBook)
        {
            project.ActiveBook.Returns(book);
            project.ActiveDraftRoot.Returns(DraftRoot);
        }
        return (new CanvasService(project, files), project, files, book);
    }

    [Fact]
    public async Task Create_WritesTheFileAndRegistersItOnTheBook()
    {
        var (sut, project, files, book) = Build();

        var canvas = await sut.CreateAsync("Act One");

        Assert.Equal("Act One", canvas.Name);
        Assert.Single(book.Canvases);
        Assert.Equal(canvas.Id, book.Canvases[0].Id);
        Assert.Single(files.Files);
        await project.Received(1).SaveProjectAsync();
    }

    [Fact]
    public async Task Create_WithoutABook_Throws()
    {
        var (sut, _, _, _) = Build(withBook: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateAsync("x"));
    }

    [Fact]
    public void GetCanvasRoot_WithoutABook_Throws()
    {
        var (sut, _, _, _) = Build(withBook: false);
        Assert.Throws<InvalidOperationException>(() => sut.GetCanvasRoot());
    }

    [Fact]
    public async Task Load_RoundTripsCardsAndConnectors()
    {
        var (sut, _, _, _) = Build();
        var created = await sut.CreateAsync("Board");
        created.Cards.Add(new CanvasCard { Id = "c1", Title = "Idea", Text = "A thought", X = 10, Y = 20 });
        created.Cards.Add(new CanvasCard { Id = "c2", Title = "Other" });
        created.Connectors.Add(new CanvasConnector
        {
            Id = "k1", FromCardId = "c1", ToCardId = "c2", Label = "because of"
        });
        await sut.SaveAsync(created);

        var loaded = await sut.LoadAsync(created.Id);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Cards.Count);
        Assert.Equal("A thought", loaded.Cards[0].Text);
        Assert.Equal(10, loaded.Cards[0].X);
        Assert.Equal("because of", loaded.Connectors[0].Label);
    }

    [Fact]
    public async Task Load_UnknownId_IsNull()
    {
        var (sut, _, _, _) = Build();
        Assert.Null(await sut.LoadAsync("nope"));
    }

    [Fact]
    public async Task Load_WithoutABook_IsNull()
    {
        var (sut, _, _, _) = Build(withBook: false);
        Assert.Null(await sut.LoadAsync("anything"));
    }

    [Fact]
    public async Task Load_ReferenceWithoutAFile_IsNull()
    {
        var (sut, _, _, book) = Build();
        book.Canvases.Add(new CanvasReference { Id = "ghost", Name = "Ghost", FileName = "ghost.json" });

        Assert.Null(await sut.LoadAsync("ghost"));
    }

    [Fact]
    public async Task Load_MalformedFile_IsNullRatherThanThrowing()
    {
        var (sut, _, files, _) = Build();
        var created = await sut.CreateAsync("Board");
        await files.WriteTextAsync(
            files.CombinePath(sut.GetCanvasRoot(), $"{created.Id}.json"), "{ not json");

        Assert.Null(await sut.LoadAsync(created.Id));
    }

    [Fact]
    public async Task Load_TakesTheNameFromTheBookNotTheFile()
    {
        var (sut, _, _, book) = Build();
        var created = await sut.CreateAsync("Original");
        // Renaming happens on the reference; the file should follow.
        book.Canvases[0].Name = "Renamed";

        var loaded = await sut.LoadAsync(created.Id);

        Assert.Equal("Renamed", loaded!.Name);
    }

    [Fact]
    public async Task Save_RenamingTheBoardUpdatesTheBook()
    {
        var (sut, project, _, book) = Build();
        var created = await sut.CreateAsync("Before");
        project.ClearReceivedCalls();

        created.Name = "After";
        await sut.SaveAsync(created);

        Assert.Equal("After", book.Canvases[0].Name);
        await project.Received(1).SaveProjectAsync();
    }

    [Fact]
    public async Task Save_WithoutARename_DoesNotRewriteTheProject()
    {
        var (sut, project, _, _) = Build();
        var created = await sut.CreateAsync("Same");
        project.ClearReceivedCalls();

        await sut.SaveAsync(created);

        await project.DidNotReceive().SaveProjectAsync();
    }

    [Fact]
    public async Task Save_UnregisteredBoard_StillWritesAFile()
    {
        // A board saved before its reference exists must not be lost.
        var (sut, _, files, _) = Build();
        await sut.SaveAsync(new CanvasData { Id = "loose", Name = "Loose" });

        Assert.Single(files.Files);
    }

    [Fact]
    public async Task Delete_RemovesTheFileAndTheReference()
    {
        var (sut, _, files, book) = Build();
        var created = await sut.CreateAsync("Board");

        Assert.True(await sut.DeleteAsync(created.Id));

        Assert.Empty(book.Canvases);
        Assert.Empty(files.Files);
    }

    [Fact]
    public async Task Delete_UnknownId_IsFalse()
    {
        var (sut, _, _, _) = Build();
        Assert.False(await sut.DeleteAsync("nope"));
    }

    [Fact]
    public async Task Delete_WithoutABook_IsFalse()
    {
        var (sut, _, _, _) = Build(withBook: false);
        Assert.False(await sut.DeleteAsync("anything"));
    }

    [Fact]
    public async Task Delete_ReferenceWithoutAFile_StillRemovesTheReference()
    {
        var (sut, _, _, book) = Build();
        book.Canvases.Add(new CanvasReference { Id = "ghost", Name = "G", FileName = "ghost.json" });

        Assert.True(await sut.DeleteAsync("ghost"));
        Assert.Empty(book.Canvases);
    }

    [Fact]
    public void List_WithoutABook_IsEmpty()
    {
        var (sut, _, _, _) = Build(withBook: false);
        Assert.Empty(sut.List());
    }

    [Fact]
    public async Task List_ReturnsCreatedBoards()
    {
        var (sut, _, _, _) = Build();
        await sut.CreateAsync("One");
        await sut.CreateAsync("Two");

        Assert.Equal(2, sut.List().Count);
    }

    [Fact]
    public async Task Create_FallsBackToTheBookRootWhenThereIsNoDraft()
    {
        var project = Substitute.For<IProjectService>();
        var files = new InMemoryFileService();
        project.ActiveBook.Returns(new BookData());
        project.ActiveDraftRoot.Returns((string?)null);
        project.ActiveBookRoot.Returns("/book");
        var sut = new CanvasService(project, files);

        await sut.CreateAsync("Board");

        Assert.Contains("Canvases", sut.GetCanvasRoot());
    }

    [Fact]
    public async Task Load_FileContainingLiteralNull_IsNull()
    {
        // "null" is valid JSON and deserialises to a null object - reachable if
        // someone edits a board file by hand.
        var (sut, _, files, _) = Build();
        var created = await sut.CreateAsync("Board");
        await files.WriteTextAsync(
            files.CombinePath(sut.GetCanvasRoot(), $"{created.Id}.json"), "null");

        Assert.Null(await sut.LoadAsync(created.Id));
    }
}
