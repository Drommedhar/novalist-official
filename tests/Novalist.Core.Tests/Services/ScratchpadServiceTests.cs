using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Notes that belong to the writer rather than to a project.
///
/// The point of this store is that using it costs nothing, so most of these are
/// about it never getting in the way: a blank note is not a note, an unknown id
/// is not an error, and a file it cannot read starts empty rather than throwing.
/// </summary>
public sealed class ScratchpadServiceTests : IDisposable
{
    private readonly string _root;

    public ScratchpadServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-scratch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private ScratchpadService Sut() => new(_root);

    [Fact]
    public async Task ANoteSurvivesTheServiceThatWroteIt()
    {
        await Sut().AddAsync("  The bridge did not exist in 1755.  ");

        // A fresh service, as though the app had been restarted - and no
        // project was ever involved.
        var note = Assert.Single(Sut().GetAll());
        Assert.Equal("The bridge did not exist in 1755.", note.Text);
    }

    [Fact]
    public async Task NewestFirst()
    {
        var sut = Sut();
        var first = await sut.AddAsync("one");
        await Task.Delay(5);
        var second = await sut.AddAsync("two");

        Assert.Equal([second!.Id, first!.Id], Sut().GetAll().Select(n => n.Id));
    }

    [Fact]
    public async Task BlankIsNotANote()
    {
        var sut = Sut();

        Assert.Null(await sut.AddAsync("   "));
        Assert.Null(await sut.AddAsync(null));
        Assert.Empty(sut.GetAll());
    }

    [Fact]
    public async Task RemovingSomethingThatIsNotThereIsNotAnError()
    {
        var sut = Sut();
        await sut.AddAsync("kept");

        await sut.RemoveAsync("no-such-id");

        Assert.Single(sut.GetAll());
        Assert.Null(sut.Find("no-such-id"));
    }

    [Fact]
    public async Task ANoteCanBeFoundAndRemoved()
    {
        var sut = Sut();
        var note = await sut.AddAsync("find me");

        Assert.Equal("find me", sut.Find(note!.Id)!.Text);

        await sut.RemoveAsync(note.Id);
        Assert.Empty(sut.GetAll());
    }

    [Fact]
    public void AnUnreadableFileStartsEmptyRatherThanThrowing()
    {
        File.WriteAllText(Path.Combine(_root, "scratchpad.json"), "{ this is not json");

        // Losing the app because a side file got corrupted would be worse than
        // losing the notes, and the file is left alone until something is added.
        Assert.Empty(Sut().GetAll());
    }
}
