using System.ComponentModel;

namespace Novalist.Sdk.Services;

/// <summary>
/// Per-extension localization service.
/// Loads locale JSON files from the extension's <c>Locales/</c> folder
/// and resolves keys with English fallback.
/// <para>XAML usage via ViewModel property:
/// <code>{Binding Loc[wordFrequencyView.title]}</code></para>
/// <para>Code usage:
/// <code>_loc.T("key")</code> or <code>_loc.T("key", arg0)</code></para>
/// </summary>
public interface IExtensionLocalization : INotifyPropertyChanged
{
    /// <summary>Indexer for XAML bindings: <c>{Binding Loc[some.key]}</c>.</summary>
    string this[string key] { get; }

    /// <summary>Get a translated string by key. Falls back to English, then to the key itself.</summary>
    string T(string key);

    /// <summary>Get a translated string with format arguments.</summary>
    string T(string key, params object[] args);
}
