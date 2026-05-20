using System;
using System.ComponentModel;
using Novalist.Core.Models;

namespace Novalist.Desktop.ViewModels;

/// <summary>
/// Read-only view of the editor state that context-aware side panels
/// (e.g. <see cref="ContextSidebarViewModel"/>) observe. Decouples those panels
/// from the WebView-bound <see cref="EditorViewModel"/> so they can be tested
/// against a lightweight fake.
/// </summary>
public interface IEditorContext : INotifyPropertyChanged
{
    bool IsDocumentOpen { get; }
    ChapterData? CurrentChapter { get; }
    SceneData? CurrentScene { get; }
    string PlainTextContent { get; }
    string Content { get; }
    string DocumentTitle { get; }
}

/// <summary>
/// Editor context plus the footnote-related hooks the
/// <see cref="FootnotesPanelViewModel"/> drives. Kept separate from
/// <see cref="IEditorContext"/> so read-only consumers need not see the
/// mutable command hooks.
/// </summary>
public interface IFootnoteEditorContext : IEditorContext
{
    event Action<string, int>? FootnoteInserted;
    Action<string>? RemoveFootnoteAction { get; set; }
    Action<string>? ScrollToFootnoteAction { get; set; }
    Action? SyncCommentsAction { get; set; }
}
