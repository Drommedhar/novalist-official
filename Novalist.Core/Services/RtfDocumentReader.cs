using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Novalist.Core.Services;

/// <summary>
/// Cross-platform RTF decoder for imported prose. It deliberately recovers
/// semantic text and formatting rather than page geometry, fonts or colours.
/// </summary>
internal sealed partial class RtfDocumentReader
{
    private enum Destination
    {
        Normal,
        Skip,
        ListText
    }

    private sealed class State
    {
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Underline { get; set; }
        public bool Strike { get; set; }
        public bool Superscript { get; set; }
        public bool Subscript { get; set; }
        public bool Hidden { get; set; }
        public int CodePage { get; set; } = 1252;
        public int UnicodeFallbackCount { get; set; } = 1;
        public ImportedTextAlignment Alignment { get; set; }
        public int HeadingLevel { get; set; }
        public int ListLevel { get; set; }
        public bool PotentialList { get; set; }
        public Destination Destination { get; set; }
        public bool AtGroupStart { get; set; }
        public bool OptionalDestination { get; set; }
        public StringBuilder? DestinationText { get; set; }

        public State Copy() => new()
        {
            Bold = Bold,
            Italic = Italic,
            Underline = Underline,
            Strike = Strike,
            Superscript = Superscript,
            Subscript = Subscript,
            Hidden = Hidden,
            CodePage = CodePage,
            UnicodeFallbackCount = UnicodeFallbackCount,
            Alignment = Alignment,
            HeadingLevel = HeadingLevel,
            ListLevel = ListLevel,
            PotentialList = PotentialList,
            Destination = Destination,
            AtGroupStart = AtGroupStart,
            OptionalDestination = OptionalDestination
        };
    }

    private readonly record struct RunStyle(
        bool Bold,
        bool Italic,
        bool Underline,
        bool Strike,
        bool Superscript,
        bool Subscript);

    private sealed class RunBuilder(RunStyle style)
    {
        public RunStyle Style { get; } = style;
        public StringBuilder Text { get; } = new();
    }

    private static readonly HashSet<string> SkippedDestinations = new(StringComparer.Ordinal)
    {
        "fonttbl", "colortbl", "stylesheet", "info", "listtable", "listoverridetable",
        "rsidtbl", "generator", "pict", "object", "objdata", "themedata",
        "colorschememapping", "latentstyles", "datastore", "xmlnstbl", "mmathpr",
        "filetbl", "revtbl", "protusertbl", "factoidname", "annotation", "atnauthor",
        "atndate", "atnicn", "atnid", "atnparent", "atnref", "atntime", "fldinst"
    };

    private static readonly object EncodingLock = new();
    private static bool _encodingProviderRegistered;

    private readonly byte[] _content;
    private readonly Stack<State> _states = new();
    private State _state = new();
    private readonly List<ImportedParagraph> _paragraphs = [];
    private readonly List<RunBuilder> _runs = [];
    private int _fallbackCharacters;
    private ImportedListKind _paragraphListKind;

    private RtfDocumentReader(byte[] content) => _content = content;

    public static ManuscriptDocument Read(byte[] content)
        => new RtfDocumentReader(content).Parse();

    public static ManuscriptDocument Read(string content)
    {
        // RTF is a byte format. Preserve the common 0-255 test/fixture form and
        // express literal UTF-16 code units with the same \u form an RTF writer
        // would use, so direct Unicode in a tolerant fixture is not destroyed.
        var bytes = new List<byte>(content.Length);
        foreach (var c in content)
        {
            if (c <= byte.MaxValue)
            {
                bytes.Add((byte)c);
                continue;
            }

            var escaped = "\\u" + unchecked((short)c).ToString(CultureInfo.InvariantCulture) + "?";
            bytes.AddRange(Encoding.ASCII.GetBytes(escaped));
        }

        return Read([.. bytes]);
    }

    private ManuscriptDocument Parse()
    {
        for (var i = 0; i < _content.Length; i++)
        {
            switch (_content[i])
            {
                case (byte)'{':
                    _states.Push(_state);
                    _state = _state.Copy();
                    _state.AtGroupStart = true;
                    _state.OptionalDestination = false;
                    _state.DestinationText = null;
                    break;

                case (byte)'}':
                    CloseGroup();
                    break;

                case (byte)'\\':
                    ReadControl(ref i);
                    break;

                case (byte)'\r':
                case (byte)'\n':
                    // Physical line wrapping in an RTF file is not prose.
                    break;

                default:
                    ReadLiteral(ref i);
                    break;
            }
        }

        EndParagraph();
        return new ManuscriptDocument { Paragraphs = _paragraphs, Format = "rtf" };
    }

    private void CloseGroup()
    {
        if (_state.Destination == Destination.ListText && _state.DestinationText != null)
            InferListKind(_state.DestinationText.ToString());

        _state = _states.Count > 0 ? _states.Pop() : new State();
    }

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

    private void AppendDestinationControl(string word, int? parameter)
    {
        if (_state.DestinationText == null) return;
        if (word == "tab") _state.DestinationText.Append('\t');
        else if (word == "bullet") _state.DestinationText.Append('\u2022');
        else if (word == "u" && parameter != null)
            _state.DestinationText.Append(unchecked((char)(ushort)parameter.Value));
    }

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
