namespace Novalist.Sdk.Hooks;

/// <summary>
/// Editor lifecycle hooks. Extensions implementing this interface are notified
/// when documents are opened and closed in the scene editor.
/// </summary>
public interface IEditorExtension
{
    /// <summary>Display name for settings / diagnostics.</summary>
    string Name { get; }

    /// <summary>Lower values load first. Default 100.</summary>
    int Priority => 100;

    /// <summary>Called when a document is opened or switched.</summary>
    void OnDocumentOpened(EditorDocumentContext context);

    /// <summary>Called when the current document is about to close or switch away.</summary>
    void OnDocumentClosing(EditorDocumentContext context);
}

/// <summary>
/// Context passed to editor extensions for every document open/close cycle.
/// </summary>
public sealed class EditorDocumentContext
{
    public required string SceneId { get; init; }
    public required string ChapterGuid { get; init; }
    public required string SceneTitle { get; init; }
    public required string ChapterTitle { get; init; }
    public required string FilePath { get; init; }
}
