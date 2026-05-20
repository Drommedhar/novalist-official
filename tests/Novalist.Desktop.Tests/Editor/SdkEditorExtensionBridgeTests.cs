using NSubstitute;
using Novalist.Desktop.Editor;
using Xunit;
using SdkHooks = Novalist.Sdk.Hooks;

namespace Novalist.Desktop.Tests.Editor;

public class SdkEditorExtensionBridgeTests
{
    [Fact]
    public void Bridge_ForwardsMetadataAndContext()
    {
        var sdk = Substitute.For<SdkHooks.IEditorExtension>();
        sdk.Name.Returns("My Ext");
        sdk.Priority.Returns(42);

        var bridge = new SdkEditorExtensionBridge(sdk);
        Assert.Equal("My Ext", bridge.Name);
        Assert.Equal(42, bridge.Priority);

        var ctx = new EditorDocumentContext
        {
            SceneId = "s1", ChapterGuid = "c1", SceneTitle = "Scene", ChapterTitle = "Chapter", FilePath = "C:/x.html",
        };

        bridge.OnDocumentOpened(ctx);
        sdk.Received().OnDocumentOpened(Arg.Is<SdkHooks.EditorDocumentContext>(c =>
            c.SceneId == "s1" && c.ChapterGuid == "c1" && c.SceneTitle == "Scene"
            && c.ChapterTitle == "Chapter" && c.FilePath == "C:/x.html"));

        bridge.OnDocumentClosing(ctx);
        sdk.Received().OnDocumentClosing(Arg.Is<SdkHooks.EditorDocumentContext>(c => c.SceneId == "s1"));
    }
}
