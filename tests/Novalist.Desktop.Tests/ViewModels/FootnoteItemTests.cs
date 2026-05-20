using Novalist.Core.Models;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

public class FootnoteItemTests
{
    [Fact]
    public void Construct_FromSource_AndCommandsFireCallbacks()
    {
        FootnoteItem? edited = null, jumped = null, deleted = null;
        var item = new FootnoteItem(
            new SceneFootnote { Id = "f1", Number = 3, Text = "note" },
            i => edited = i, i => jumped = i, i => deleted = i);

        Assert.Equal("f1", item.Id);
        Assert.Equal(3, item.Number);
        Assert.Equal("note", item.Text);

        item.Text = "changed";       // OnTextChanged -> callback
        Assert.Same(item, edited);

        item.JumpCommand.Execute(null);
        Assert.Same(item, jumped);

        item.DeleteCommand.Execute(null);
        Assert.Same(item, deleted);
    }

    [Fact]
    public void NullCallbacks_NoThrow()
    {
        var item = new FootnoteItem(new SceneFootnote { Id = "f", Number = 1, Text = "t" }, null, null, null);
        item.Text = "x";
        item.JumpCommand.Execute(null);
        item.DeleteCommand.Execute(null);
    }
}
