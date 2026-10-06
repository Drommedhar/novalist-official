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
}
