using System.Collections.Generic;
using Avalonia.Input;

namespace Novalist.Desktop.Utilities;

/// <summary>
/// Maps JavaScript KeyboardEvent.code / key values to Avalonia <see cref="Key"/> enum values.
/// Used to forward hotkey presses from WebView-hosted editors back to the host app.
/// </summary>
internal static class WebViewKeyMapper
{
    private static readonly Dictionary<string, Key> CodeMap = new()
    {
        // Letters
        ["KeyA"] = Key.A, ["KeyB"] = Key.B, ["KeyC"] = Key.C, ["KeyD"] = Key.D,
        ["KeyE"] = Key.E, ["KeyF"] = Key.F, ["KeyG"] = Key.G, ["KeyH"] = Key.H,
        ["KeyI"] = Key.I, ["KeyJ"] = Key.J, ["KeyK"] = Key.K, ["KeyL"] = Key.L,
        ["KeyM"] = Key.M, ["KeyN"] = Key.N, ["KeyO"] = Key.O, ["KeyP"] = Key.P,
        ["KeyQ"] = Key.Q, ["KeyR"] = Key.R, ["KeyS"] = Key.S, ["KeyT"] = Key.T,
        ["KeyU"] = Key.U, ["KeyV"] = Key.V, ["KeyW"] = Key.W, ["KeyX"] = Key.X,
        ["KeyY"] = Key.Y, ["KeyZ"] = Key.Z,

        // Digits
        ["Digit0"] = Key.D0, ["Digit1"] = Key.D1, ["Digit2"] = Key.D2,
        ["Digit3"] = Key.D3, ["Digit4"] = Key.D4, ["Digit5"] = Key.D5,
        ["Digit6"] = Key.D6, ["Digit7"] = Key.D7, ["Digit8"] = Key.D8,
        ["Digit9"] = Key.D9,

        // Punctuation / OEM keys
        ["Comma"] = Key.OemComma,
        ["Period"] = Key.OemPeriod,
        ["Semicolon"] = Key.OemSemicolon,
        ["Quote"] = Key.OemQuotes,
        ["BracketLeft"] = Key.OemOpenBrackets,
        ["BracketRight"] = Key.OemCloseBrackets,
        ["Backquote"] = Key.OemTilde,
        ["Backslash"] = Key.OemBackslash,
        ["Slash"] = Key.OemQuestion,
        ["Minus"] = Key.OemMinus,
        ["Equal"] = Key.OemPlus,

        // Function keys
        ["F1"] = Key.F1, ["F2"] = Key.F2, ["F3"] = Key.F3, ["F4"] = Key.F4,
        ["F5"] = Key.F5, ["F6"] = Key.F6, ["F7"] = Key.F7, ["F8"] = Key.F8,
        ["F9"] = Key.F9, ["F10"] = Key.F10, ["F11"] = Key.F11, ["F12"] = Key.F12,

        // Special keys
        ["Space"] = Key.Space,
        ["Enter"] = Key.Enter,
        ["Tab"] = Key.Tab,
        ["Escape"] = Key.Escape,
        ["Backspace"] = Key.Back,
        ["Delete"] = Key.Delete,
        ["Insert"] = Key.Insert,
        ["Home"] = Key.Home,
        ["End"] = Key.End,
        ["PageUp"] = Key.PageUp,
        ["PageDown"] = Key.PageDown,
        ["ArrowUp"] = Key.Up,
        ["ArrowDown"] = Key.Down,
        ["ArrowLeft"] = Key.Left,
        ["ArrowRight"] = Key.Right,
    };

    public static Key MapToAvaloniaKey(string code, string key)
    {
        if (CodeMap.TryGetValue(code, out var mapped))
            return mapped;

        // Fallback: try the key value for single characters
        if (key.Length == 1)
        {
            var ch = char.ToUpperInvariant(key[0]);
            if (ch >= 'A' && ch <= 'Z')
                return (Key)(Key.A + (ch - 'A'));
            if (ch >= '0' && ch <= '9')
                return (Key)(Key.D0 + (ch - '0'));
        }

        return Key.None;
    }
}
