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

    private sealed class MinimalSchemaContributor : ISettingsSchemaContributor
    {
        public SettingsSchema GetSettingsSchema() => new();
        public System.Threading.Tasks.Task ApplySettingsAsync(
            System.Collections.Generic.IReadOnlyDictionary<string, string> values)
            => System.Threading.Tasks.Task.CompletedTask;
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

    [Fact]
    public async System.Threading.Tasks.Task ISettingsSchemaContributor_ExecuteAction_DefaultsToNull()
    {
        ISettingsSchemaContributor c = new MinimalSchemaContributor();
        var result = await c.ExecuteSchemaActionAsync(
            "any", new System.Collections.Generic.Dictionary<string, string>());
        Assert.Null(result);
    }
}
