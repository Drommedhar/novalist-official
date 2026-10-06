using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Novalist.Core.Services;

internal sealed partial class RtfDocumentReader
{
    private void ReadLiteral(ref int index)
    {
        var start = index;
        while (index + 1 < _content.Length && _content[index + 1] is not ((byte)'{')
               and not ((byte)'}') and not ((byte)'\\') and not ((byte)'\r') and not ((byte)'\n'))
            index++;

        var bytes = _content.AsSpan(start, index - start + 1);
        if (_state.Destination == Destination.Skip) return;

        var text = Decode(bytes, _state.CodePage);
        if (_state.Destination == Destination.ListText)
        {
            _state.DestinationText?.Append(text);
            return;
        }

        AppendCharacterToken(text);
        _state.AtGroupStart = false;
    }

    private void AppendEncodedByte(byte value)
    {
        if (_state.Destination == Destination.ListText)
        {
            if (_fallbackCharacters > 0) _fallbackCharacters--;
            else _state.DestinationText?.Append(Decode([value], _state.CodePage));
            return;
        }

        AppendCharacterToken(Decode([value], _state.CodePage));
    }

    private void AppendUnicode(int value)
    {
        var character = unchecked((char)(ushort)value);
        if (character is '\0' or '\uFFFE' or '\uFFFF') return;
        AppendText(character.ToString());
    }

    private void AppendCharacterToken(string value)
    {
        if (value.Length == 0) return;
        if (_fallbackCharacters > 0)
        {
            // The fallback is normally one ASCII character or one \'hh token.
            // Consume characters, not bytes, exactly as \ucN specifies.
            var skip = Math.Min(_fallbackCharacters, value.Length);
            _fallbackCharacters -= skip;
            value = value[skip..];
        }

        AppendText(value);
    }

    private void AppendText(string value)
    {
        if (_state.Hidden || _state.Destination != Destination.Normal || value.Length == 0) return;

        var clean = new string([.. value.Where(c => c is '\t' or '\n' || (c >= ' ' && c != '\u007F'))]);
        if (clean.Length == 0) return;

        var style = new RunStyle(
            _state.Bold, _state.Italic, _state.Underline, _state.Strike,
            _state.Superscript, _state.Subscript);
        if (_runs.Count == 0 || _runs[^1].Style != style) _runs.Add(new RunBuilder(style));
        _runs[^1].Text.Append(clean);
    }

    private void EndParagraph()
    {
        TrimRuns();
        if (_runs.Count == 0)
        {
            ResetParagraphState();
            return;
        }

        var runs = _runs.Select(r => new ImportedTextRun(
            r.Text.ToString(), r.Style.Bold, r.Style.Italic, r.Style.Underline,
            r.Style.Strike, r.Style.Superscript, r.Style.Subscript)).ToList();
        var text = string.Concat(runs.Select(r => r.Text));
        if (text.Length > 0)
        {
            _paragraphs.Add(SceneBreakRegex().IsMatch(text)
                ? new ImportedParagraph { IsSceneBreak = true }
                : new ImportedParagraph
                {
                    Text = text,
                    Runs = runs,
                    HeadingLevel = _state.HeadingLevel,
                    ListKind = _paragraphListKind,
                    ListLevel = _state.ListLevel,
                    Alignment = _state.Alignment
                });
        }

        _runs.Clear();
        ResetParagraphState();
    }

    private void ResetParagraphState()
    {
        _paragraphListKind = ImportedListKind.None;
        _fallbackCharacters = 0;
    }

    private void TrimRuns()
    {
        while (_runs.Count > 0)
        {
            var trimmed = _runs[0].Text.ToString().TrimStart();
            if (trimmed.Length == 0)
            {
                _runs.RemoveAt(0);
                continue;
            }

            _runs[0].Text.Clear();
            _runs[0].Text.Append(trimmed);
            break;
        }

        while (_runs.Count > 0)
        {
            var last = _runs[^1];
            var trimmed = last.Text.ToString().TrimEnd();
            if (trimmed.Length == 0)
            {
                _runs.RemoveAt(_runs.Count - 1);
                continue;
            }

            last.Text.Clear();
            last.Text.Append(trimmed);
            break;
        }
    }

    private void InferListKind(string marker)
    {
        var trimmed = marker.Trim();
        if (trimmed.Length == 0)
        {
            if (_state.PotentialList) _paragraphListKind = ImportedListKind.Unordered;
            return;
        }

        _paragraphListKind = OrderedListMarkerRegex().IsMatch(trimmed)
            ? ImportedListKind.Ordered
            : ImportedListKind.Unordered;
    }

    private static string Decode(ReadOnlySpan<byte> bytes, int codePage)
    {
        EnsureEncodingProvider();
        try
        {
            return Encoding.GetEncoding(codePage).GetString(bytes);
        }
        catch (ArgumentException)
        {
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    private static void EnsureEncodingProvider()
    {
        if (_encodingProviderRegistered) return;
        lock (EncodingLock)
        {
            if (_encodingProviderRegistered) return;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _encodingProviderRegistered = true;
        }
    }

    private static bool IsAsciiLetter(byte value)
        => value is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z';

    private static bool IsAsciiDigit(byte value) => value is >= (byte)'0' and <= (byte)'9';

    private static bool TryHex(byte value, out int result)
    {
        if (value is >= (byte)'0' and <= (byte)'9')
        {
            result = value - '0';
            return true;
        }

        if (value is >= (byte)'a' and <= (byte)'f')
        {
            result = value - 'a' + 10;
            return true;
        }

        if (value is >= (byte)'A' and <= (byte)'F')
        {
            result = value - 'A' + 10;
            return true;
        }

        result = 0;
        return false;
    }

    [GeneratedRegex(@"^(?:\d+|[a-z]+|[ivxlcdm]+)[.)]", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex OrderedListMarkerRegex();

    [GeneratedRegex(@"^\s*(\*\s*){3,}\s*$|^\s*#\s*$|^\s*-{3,}\s*$|^\s*_{3,}\s*$", RegexOptions.Compiled)]
    private static partial Regex SceneBreakRegex();
}
