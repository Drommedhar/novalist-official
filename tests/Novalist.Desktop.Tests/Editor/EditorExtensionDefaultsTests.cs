using Novalist.Desktop.Editor;
using Xunit;

namespace Novalist.Desktop.Tests.Editor;

public class EditorExtensionDefaultsTests
{
    // An IEditorExtension that does not override Priority -> exercises the
    // interface's default `Priority => 100`.
    private sealed class MinimalExtension : IEditorExtension
    {
        public string Name => "Minimal";
        public void OnDocumentOpened(EditorDocumentContext context) { }
        public void OnDocumentClosing(EditorDocumentContext context) { }
    }

    [Fact]
    public void DefaultPriority_Is100()
    {
        IEditorExtension ext = new MinimalExtension();
        Assert.Equal(100, ext.Priority);
        Assert.Equal("Minimal", ext.Name);
    }
}
