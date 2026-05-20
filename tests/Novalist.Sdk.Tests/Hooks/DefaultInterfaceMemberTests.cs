using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;
using Xunit;

namespace Novalist.Sdk.Tests.Hooks;

public class DefaultInterfaceMemberTests
{
    // Minimal implementation that does NOT override the default members,
    // so calling through the interface executes the default bodies.
    private sealed class MinimalAiHook : IAiHook
    {
        public string? OnBuildSystemPrompt(AiPromptContext context) => null;
    }

    private sealed class MinimalEditorExtension : IEditorExtension
    {
        public string Name => "minimal";
        public void OnDocumentOpened(EditorDocumentContext context) { }
        public void OnDocumentClosing(EditorDocumentContext context) { }
    }

    [Fact]
    public void IAiHook_OnResponseChunk_DefaultPassesThrough()
    {
        IAiHook hook = new MinimalAiHook();
        Assert.Equal("chunk", hook.OnResponseChunk("chunk"));
    }

    [Fact]
    public void IEditorExtension_Priority_DefaultsTo100()
    {
        IEditorExtension ext = new MinimalEditorExtension();
        Assert.Equal(100, ext.Priority);
    }
}
