using System.Collections.Generic;
using System.Linq;

namespace Novalist.Desktop.Editor;

/// <summary>
/// Manages the lifecycle of editor extensions.
/// </summary>
public sealed class EditorExtensionManager
{
    private readonly List<IEditorExtension> _extensions = new();
    private EditorDocumentContext? _currentContext;

    public IReadOnlyList<IEditorExtension> Extensions => _extensions;

    public void Register(IEditorExtension extension)
    {
        _extensions.Add(extension);
        _extensions.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        if (_currentContext != null)
        {
            extension.OnDocumentOpened(_currentContext);
        }
    }

    public void Unregister(IEditorExtension extension)
    {
        if (_currentContext != null) extension.OnDocumentClosing(_currentContext);
        _extensions.Remove(extension);
    }

    public void NotifyDocumentOpened(EditorDocumentContext context)
    {
        _currentContext = context;
        foreach (var ext in _extensions)
        {
            ext.OnDocumentOpened(context);
        }
    }

    public void NotifyDocumentClosing()
    {
        if (_currentContext == null) return;
        foreach (var ext in _extensions)
        {
            ext.OnDocumentClosing(_currentContext);
        }
        _currentContext = null;
    }

    public void Shutdown()
    {
        if (_currentContext != null) NotifyDocumentClosing();
        _extensions.Clear();
    }
}
