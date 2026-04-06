namespace Novalist.Desktop.Editor;

/// <summary>
/// Bridges an SDK <see cref="Sdk.Hooks.IEditorExtension"/> to the Desktop
/// <see cref="IEditorExtension"/> so extensions loaded from assemblies
/// integrate seamlessly with the existing <see cref="EditorExtensionManager"/>.
/// </summary>
internal sealed class SdkEditorExtensionBridge : IEditorExtension
{
    private readonly Sdk.Hooks.IEditorExtension _sdkExtension;

    public SdkEditorExtensionBridge(Sdk.Hooks.IEditorExtension sdkExtension)
    {
        _sdkExtension = sdkExtension;
    }

    public string Name => _sdkExtension.Name;
    public int Priority => _sdkExtension.Priority;

    public void OnDocumentOpened(EditorDocumentContext context)
    {
        _sdkExtension.OnDocumentOpened(new Sdk.Hooks.EditorDocumentContext
        {
            SceneId = context.SceneId,
            ChapterGuid = context.ChapterGuid,
            SceneTitle = context.SceneTitle,
            ChapterTitle = context.ChapterTitle,
            FilePath = context.FilePath
        });
    }

    public void OnDocumentClosing(EditorDocumentContext context)
    {
        _sdkExtension.OnDocumentClosing(new Sdk.Hooks.EditorDocumentContext
        {
            SceneId = context.SceneId,
            ChapterGuid = context.ChapterGuid,
            SceneTitle = context.SceneTitle,
            ChapterTitle = context.ChapterTitle,
            FilePath = context.FilePath
        });
    }
}
