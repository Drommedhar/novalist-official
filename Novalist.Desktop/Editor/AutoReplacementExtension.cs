using System.Collections.Generic;
using Novalist.Core.Models;

namespace Novalist.Desktop.Editor;

/// <summary>
/// Provides auto-replacement configuration (smart quotes, em-dash, ellipsis).
/// The actual replacement logic runs in JavaScript inside the WebView editor.
/// This extension just holds the configuration and serializes it for JS.
/// </summary>
public sealed class AutoReplacementExtension : IEditorExtension
{
    private List<AutoReplacementPair> _pairs = [];

    public string Name => "AutoReplacement";
    public int Priority => 50;

    public List<AutoReplacementPair> Pairs
    {
        get => _pairs;
        set => _pairs = value ?? [];
    }

    /// <summary>
    /// Serializes the current replacement pairs as a JSON array for the JS editor.
    /// </summary>
    public string SerializePairsJson()
    {
        if (_pairs.Count == 0) return "[]";
        var sb = new System.Text.StringBuilder("[");
        for (int i = 0; i < _pairs.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"start\":");
            sb.Append(JsonEscape(_pairs[i].Start));
            sb.Append(",\"end\":");
            sb.Append(JsonEscape(_pairs[i].End));
            sb.Append(",\"startReplace\":");
            sb.Append(JsonEscape(_pairs[i].StartReplace));
            sb.Append(",\"endReplace\":");
            sb.Append(JsonEscape(_pairs[i].EndReplace));
            sb.Append('}');
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static string JsonEscape(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        return "\"" + value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t") + "\"";
    }

    public void OnDocumentOpened(EditorDocumentContext context) { }
    public void OnDocumentClosing(EditorDocumentContext context) { }
}
