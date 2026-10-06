using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Novalist.Core.Services;

internal sealed partial class RtfDocumentReader
{
    private void ReadControl(ref int index)
    {
        if (index + 1 >= _content.Length) return;
        var next = _content[++index];

        if (!IsAsciiLetter(next))
        {
            ReadControlSymbol(next, ref index);
            return;
        }

        var start = index;
        while (index + 1 < _content.Length && IsAsciiLetter(_content[index + 1])) index++;
        var word = Encoding.ASCII.GetString(_content, start, index - start + 1).ToLowerInvariant();

        int? parameter = null;
        var sign = 1;
        var cursor = index + 1;
        if (cursor < _content.Length && _content[cursor] == (byte)'-')
        {
            sign = -1;
            cursor++;
        }

        var numberStart = cursor;
        while (cursor < _content.Length && IsAsciiDigit(_content[cursor])) cursor++;
        if (cursor > numberStart)
        {
            var value = Encoding.ASCII.GetString(_content, numberStart, cursor - numberStart);
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                parameter = parsed * sign;
            index = cursor - 1;
        }

        if (index + 1 < _content.Length && _content[index + 1] == (byte)' ') index++;

        if (_state.AtGroupStart)
        {
            if (_state.OptionalDestination)
                _state.Destination = Destination.Skip;
            else if (word is "listtext" or "pntext")
            {
                _state.Destination = Destination.ListText;
                _state.DestinationText = new StringBuilder();
            }
            else if (SkippedDestinations.Contains(word))
                _state.Destination = Destination.Skip;

            _state.AtGroupStart = false;
        }

        if (_state.Destination == Destination.Skip) return;

        if (_state.Destination == Destination.ListText)
        {
            AppendDestinationControl(word, parameter);
            return;
        }

        ApplyControl(word, parameter, ref index);
    }

    private void ReadControlSymbol(byte symbol, ref int index)
    {
        if (symbol == (byte)'*' && _state.AtGroupStart)
        {
            _state.OptionalDestination = true;
            return;
        }

        _state.AtGroupStart = false;
        if (_state.Destination == Destination.Skip) return;

        if (symbol == (byte)'\'' && index + 2 < _content.Length
            && TryHex(_content[index + 1], out var high)
            && TryHex(_content[index + 2], out var low))
        {
            index += 2;
            AppendEncodedByte((byte)((high << 4) | low));
            return;
        }

        var value = symbol switch
        {
            (byte)'\\' => "\\",
            (byte)'{' => "{",
            (byte)'}' => "}",
            (byte)'~' => "\u00A0",
            (byte)'-' => "\u00AD",
            (byte)'_' => "\u2011",
            _ => string.Empty
        };
        AppendCharacterToken(value);
    }

    private void ApplyControl(string word, int? parameter, ref int index)
    {
        switch (word)
        {
            case "rtf":
            case "ansi":
                break;
            case "mac":
                _state.CodePage = 10000;
                break;
            case "pc":
                _state.CodePage = 437;
                break;
            case "pca":
                _state.CodePage = 850;
                break;
            case "ansicpg" when parameter is > 0:
                _state.CodePage = parameter.Value;
                break;
            case "uc" when parameter is >= 0:
                _state.UnicodeFallbackCount = parameter.Value;
                break;
            case "u" when parameter != null:
                AppendUnicode(parameter.Value);
                _fallbackCharacters = _state.UnicodeFallbackCount;
                break;
            case "bin" when parameter is > 0:
                index = Math.Min(_content.Length - 1, index + parameter.Value);
                break;
            case "par":
                EndParagraph();
                break;
            case "line":
            case "softline":
                AppendCharacterToken("\n");
                break;
            case "tab":
                AppendCharacterToken("\t");
                break;
            case "emdash": AppendCharacterToken("\u2014"); break;
            case "endash": AppendCharacterToken("\u2013"); break;
            case "emspace": AppendCharacterToken("\u2003"); break;
            case "enspace": AppendCharacterToken("\u2002"); break;
            case "qmspace": AppendCharacterToken("\u2005"); break;
            case "bullet": AppendCharacterToken("\u2022"); break;
            case "lquote": AppendCharacterToken("\u2018"); break;
            case "rquote": AppendCharacterToken("\u2019"); break;
            case "ldblquote": AppendCharacterToken("\u201C"); break;
            case "rdblquote": AppendCharacterToken("\u201D"); break;
            default:
                ApplyFormattingControl(word, parameter);
                break;
        }
    }

    private void AppendDestinationControl(string word, int? parameter)
    {
        if (_state.DestinationText == null) return;
        if (word == "tab") _state.DestinationText.Append('\t');
        else if (word == "bullet") _state.DestinationText.Append('\u2022');
        else if (word == "u" && parameter != null)
            _state.DestinationText.Append(unchecked((char)(ushort)parameter.Value));
    }

    private void ApplyFormattingControl(string word, int? parameter)
    {
        switch (word)
        {
            case "b": _state.Bold = parameter != 0; break;
            case "i": _state.Italic = parameter != 0; break;
            case "ul": _state.Underline = parameter != 0; break;
            case "ulnone": _state.Underline = false; break;
            case "strike": _state.Strike = parameter != 0; break;
            case "super":
                _state.Superscript = true;
                _state.Subscript = false;
                break;
            case "sub":
                _state.Subscript = true;
                _state.Superscript = false;
                break;
            case "nosupersub":
                _state.Superscript = false;
                _state.Subscript = false;
                break;
            case "v": _state.Hidden = parameter != 0; break;
            case "plain":
                _state.Bold = false;
                _state.Italic = false;
                _state.Underline = false;
                _state.Strike = false;
                _state.Superscript = false;
                _state.Subscript = false;
                _state.Hidden = false;
                break;
            case "pard":
                _state.Alignment = ImportedTextAlignment.Default;
                _state.HeadingLevel = 0;
                _state.ListLevel = 0;
                _state.PotentialList = false;
                _paragraphListKind = ImportedListKind.None;
                break;
            case "ql": _state.Alignment = ImportedTextAlignment.Left; break;
            case "qc": _state.Alignment = ImportedTextAlignment.Center; break;
            case "qr": _state.Alignment = ImportedTextAlignment.Right; break;
            case "qj": _state.Alignment = ImportedTextAlignment.Justify; break;
            case "outlinelevel" when parameter is >= 0:
                _state.HeadingLevel = parameter.Value + 1;
                break;
            case "ls":
                _state.PotentialList = parameter != 0;
                break;
            case "ilvl" when parameter is >= 0:
                _state.ListLevel = parameter.Value;
                break;
            case "page":
            case "sect":
                EndParagraph();
                break;
        }
    }
}
