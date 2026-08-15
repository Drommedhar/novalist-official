using System.Text;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// RTF is a byte-oriented state machine, not markup that can be recovered by
/// dropping backslash words. These cases are the control forms emitted by real
/// Scrivener and word-processor files.
/// </summary>
public sealed class RtfDocumentReaderTests
{
    private static ManuscriptDocument Read(string rtf) => ManuscriptReader.ReadRtf(rtf);

    private static ImportedParagraph Only(string rtf) => Assert.Single(Read(rtf).Paragraphs);

    [Fact]
    public void UnicodeValuesReplaceTheirAnsiFallbackExactlyOnce()
    {
        var paragraph = Only(
            @"{\rtf1\ansi\ansicpg1252\uc1 \u8220\'93Hello\u8221\'94 It\u8217\'92s\emdash done\u8230\'85\par}");

        Assert.Equal("“Hello” It’s—done…", paragraph.Text);
        Assert.DoesNotContain("'93", paragraph.Text);
    }

    [Fact]
    public void SignedUnicodeCodeUnitsCanFormASurrogatePair()
    {
        var paragraph = Only(@"{\rtf1\ansi\uc0 Smile: \u-10179\u-8704\par}");

        Assert.Equal("Smile: 😀", paragraph.Text);
    }

    [Fact]
    public void LiteralBytesUseTheCodePageDeclaredByTheDocument()
    {
        var prefix = Encoding.ASCII.GetBytes(@"{\rtf1\ansi\ansicpg1252 caf");
        var suffix = Encoding.ASCII.GetBytes(@"\par}");
        var bytes = prefix.Concat(new byte[] { 0xE9 }).Concat(suffix).ToArray();

        var paragraph = Assert.Single(ManuscriptReader.ReadRtf(bytes).Paragraphs);

        Assert.Equal("café", paragraph.Text);
        Assert.DoesNotContain('�', paragraph.Text);
    }

    [Fact]
    public void EscapedRtfSyntaxRemainsLiteralText()
    {
        var paragraph = Only(@"{\rtf1 Path C:\\Draft contains \{notes\}.\par}");

        Assert.Equal(@"Path C:\Draft contains {notes}.", paragraph.Text);
    }

    [Fact]
    public void OptionalAndKnownMetadataDestinationsNeverLeakIntoProse()
    {
        var paragraph = Only(
            @"{\rtf1{\*\unknown Never visible}{\fonttbl{\f0 Times;}}{\*\listtable Hidden too}\f0 Real prose.\par}");

        Assert.Equal("Real prose.", paragraph.Text);
        Assert.DoesNotContain('*', paragraph.Text);
    }

    [Fact]
    public void CharacterFormattingIsScopedAndRenderedAsSafeCanonicalHtml()
    {
        var document = Read(
            @"{\rtf1 Plain {\b bold {\i both} bold} plain {\ul under} {\strike struck}.\par}");
        var paragraph = Assert.Single(document.Paragraphs);
        var plan = ManuscriptSplitter.Split(document);
        var html = plan.Chapters[0].Scenes[0].Html;

        Assert.Equal("Plain bold both bold plain under struck.", paragraph.Text);
        Assert.Contains("font-weight:bold", html);
        Assert.Contains("font-style:italic", html);
        Assert.Contains("text-decoration:underline", html);
        Assert.Contains("text-decoration:line-through", html);
        Assert.Contains("Plain ", html);
    }

    [Fact]
    public void ParagraphResetDoesNotInventAParagraphAndLineIsASoftBreak()
    {
        var document = Read(@"{\rtf1 One \pard two\line three.\par}");

        var paragraph = Assert.Single(document.Paragraphs);
        Assert.Equal("One two\nthree.", paragraph.Text);
        Assert.Contains("One two<br>three.", ManuscriptSplitter.Split(document).Chapters[0].Scenes[0].Html);
    }

    [Fact]
    public void RealStyleNumberListTextBecomesListStructureNotVisibleNumberText()
    {
        var document = Read(
            @"{\rtf1\ansi{\*\listtable{\list\listtemplateid1{\listlevel\levelnfc0\levelstartat1}}}{\*\listoverridetable{\listoverride\listid1\ls1}}\pard\ls1\ilvl0{\listtext 1.\tab}First item\par\pard\ls1\ilvl0{\listtext 2.\tab}Second item\par}");

        Assert.Equal([ImportedListKind.Ordered, ImportedListKind.Ordered],
            document.Paragraphs.Select(p => p.ListKind));
        Assert.Equal(["First item", "Second item"], document.Paragraphs.Select(p => p.Text));
        var html = ManuscriptSplitter.Split(document).Chapters[0].Scenes[0].Html;
        Assert.Equal("<ol><li>First item</li><li>Second item</li></ol>", html);
    }

    [Fact]
    public void HiddenAndBinaryPayloadsAreNotImported()
    {
        var paragraph = Only(@"{\rtf1 Visible {\v secret} \bin4 abcdsafe.\par}");

        Assert.Equal("Visible  safe.", paragraph.Text);
        Assert.DoesNotContain("secret", paragraph.Text);
        Assert.DoesNotContain("abcd", paragraph.Text);
    }

    [Fact]
    public void LiteralHtmlIsEncodedRatherThanTrusted()
    {
        var plan = ManuscriptSplitter.Split(Read(@"{\rtf1 <script>alert(1)</script> & prose.\par}"));
        var html = plan.Chapters[0].Scenes[0].Html;

        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&amp; prose", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void TruncatedGroupsRemainReadableRatherThanThrowing()
    {
        var document = Read(@"{\rtf1 Start {\b unfinished");

        Assert.Equal("Start unfinished", Assert.Single(document.Paragraphs).Text);
    }

    [Fact]
    public void LiteralUnicodeInAToleranltFixtureSurvivesTheByteRoundTrip()
    {
        // A fixture written as a C# string is not RTF bytes. Characters past
        // Latin-1 have to reach the parser as the \u form a writer would emit,
        // or a CJK sample silently imports as mojibake.
        var paragraph = Only("{\\rtf1 日本\\par}");

        Assert.Equal("日本", paragraph.Text);
    }

    [Fact]
    public void PunctuationControlSymbolsBecomeTheCharactersTheyStandFor()
    {
        // \~ non-breaking space, \- optional hyphen, \_ non-breaking hyphen.
        // An unknown symbol contributes nothing rather than a stray glyph.
        var paragraph = Only(@"{\rtf1 A\~B\-C\_D\@E\par}");

        Assert.Equal("A B­C‑DE", paragraph.Text);
    }

    [Theory]
    [InlineData(@"\mac", 0xD5, '’')]   // Mac Roman right single quote
    [InlineData(@"\pc", 0x9B, '¢')]    // CP437 cent sign
    [InlineData(@"\pca", 0x9B, 'ø')]   // CP850 o with stroke
    public void LegacyCodePageDeclarationsDecodeTheirOwnHighBytes(
        string control, int highByte, char expected)
    {
        var prefix = Encoding.ASCII.GetBytes(@"{\rtf1" + control + " X");
        var suffix = Encoding.ASCII.GetBytes(@"\par}");
        var bytes = prefix.Concat(new[] { (byte)highByte }).Concat(suffix).ToArray();

        var paragraph = Assert.Single(ManuscriptReader.ReadRtf(bytes).Paragraphs);

        Assert.Equal("X" + expected, paragraph.Text);
        Assert.DoesNotContain('�', paragraph.Text);
    }

    [Fact]
    public void NamedTypographicControlsBecomeRealPunctuationAndSpacing()
    {
        var paragraph = Only(
            @"{\rtf1 \endash\emspace\enspace\qmspace\bullet\lquote\rquote\ldblquote\rdblquote X\par}");

        Assert.Equal("–   •‘’“”X", paragraph.Text);
    }

    [Fact]
    public void UnderlineCanBeTurnedOffWithoutEndingTheGroup()
    {
        // Two spaces: the first delimits the control word, the second is prose.
        var document = Read(@"{\rtf1 {\ul under\ulnone  plain}\par}");
        var html = ManuscriptSplitter.Split(document).Chapters[0].Scenes[0].Html;

        Assert.Equal("under plain", Assert.Single(document.Paragraphs).Text);
        Assert.Contains("<span style=\"text-decoration:underline\">under</span>", html);
        Assert.Contains("plain", html);
    }

    [Fact]
    public void SuperscriptAndSubscriptAreScopedAndCanBeCancelled()
    {
        var document = Read(@"{\rtf1 E{\super 2}\nosupersub  and H{\sub 2}O\par}");
        var html = ManuscriptSplitter.Split(document).Chapters[0].Scenes[0].Html;

        Assert.Equal("E2 and H2O", Assert.Single(document.Paragraphs).Text);
        Assert.Contains("<sup>2</sup>", html);
        Assert.Contains("<sub>2</sub>", html);
    }

    [Theory]
    [InlineData(@"\qr", ImportedTextAlignment.Right, "right")]
    [InlineData(@"\qj", ImportedTextAlignment.Justify, "justify")]
    public void AlignmentThatCarriesMeaningReachesTheStoredHtml(
        string control, ImportedTextAlignment expected, string css)
    {
        var document = Read(@"{\rtf1\pard" + control + @" Aligned prose.\par}");
        var html = ManuscriptSplitter.Split(document).Chapters[0].Scenes[0].Html;

        Assert.Equal(expected, Assert.Single(document.Paragraphs).Alignment);
        Assert.Equal($"<p style=\"text-align:{css}\">Aligned prose.</p>", html);
    }

    [Fact]
    public void OutlineLevelIsTheHeadingLevelTheDocumentStructureIsBuiltFrom()
    {
        var document = Read(@"{\rtf1\pard\outlinelevel0 Chapter One\par\pard Body text.\par}");

        Assert.Equal([1, 0], document.Paragraphs.Select(p => p.HeadingLevel));
    }

    [Fact]
    public void PageAndSectionBreaksEndTheParagraphTheyInterrupt()
    {
        var document = Read(@"{\rtf1 One\page Two\sect Three\par}");

        Assert.Equal(["One", "Two", "Three"], document.Paragraphs.Select(p => p.Text));
    }

    [Theory]
    [InlineData(@"\'e9", "café")]   // lower-case hex, which is what most writers emit
    [InlineData(@"\'E9", "café")]   // upper-case hex, which Word emits
    [InlineData(@"\'zz", "cafzz")]  // not hex at all: literal, never a mangled glyph
    public void HexEscapesAreReadInEitherCaseAndSurviveBeingMalformed(
        string escape, string expected)
    {
        Assert.Equal(expected, Only(@"{\rtf1\ansi\ansicpg1252 caf" + escape + @"\par}").Text);
    }

    [Fact]
    public void AnImpossibleCodePageFallsBackRatherThanLosingTheParagraph()
    {
        var prefix = Encoding.ASCII.GetBytes(@"{\rtf1\ansicpg99999 caf");
        var bytes = prefix.Concat(new byte[] { 0xE9 })
            .Concat(Encoding.ASCII.GetBytes(@"\par}")).ToArray();

        var paragraph = Assert.Single(ManuscriptReader.ReadRtf(bytes).Paragraphs);

        Assert.Equal("café", paragraph.Text);
    }

    [Fact]
    public void StyledWhitespaceAtEitherEndIsNotAParagraphOfItsOwn()
    {
        // Scrivener wraps stray spaces in the run that carried the last style,
        // so the trim has to survive removing whole runs from both ends.
        var paragraph = Only(@"{\rtf1 {\b   }Text{\i   }\par}");

        Assert.Equal("Text", paragraph.Text);
    }

    [Fact]
    public void AListWithNoVisibleMarkerIsStillAListBecauseTheStyleSaysSo()
    {
        var document = Read(@"{\rtf1\ansi\pard\ls1\ilvl0{\listtext \tab}Item\par}");

        Assert.Equal(ImportedListKind.Unordered, Assert.Single(document.Paragraphs).ListKind);
        Assert.Equal("<ul><li>Item</li></ul>",
            ManuscriptSplitter.Split(document).Chapters[0].Scenes[0].Html);
    }

    [Fact]
    public void AByteMarkerIsDecodedIntoTheListKindItStandsFor()
    {
        var document = Read(@"{\rtf1\ansi\pard\ls1\ilvl0{\listtext \'95\tab}Bulleted\par}");

        Assert.Equal(ImportedListKind.Unordered, Assert.Single(document.Paragraphs).ListKind);
        Assert.Equal("Bulleted", document.Paragraphs[0].Text);
    }

    [Fact]
    public void APendingUnicodeFallbackIsSwallowedByTheListMarkerNotPrintedIntoIt()
    {
        // A \u bullet with no fallback of its own leaves one character owed when
        // the marker group opens. The owed \'31 is the digit that would
        // otherwise make this an ordered list, so the kind proves it was eaten.
        var document = Read(@"{\rtf1\ansi\uc1\pard\ls1\ilvl0 \" + @"u8226{\listtext \'31.\tab}Item\par}");

        var paragraph = Assert.Single(document.Paragraphs);
        Assert.Equal(ImportedListKind.Unordered, paragraph.ListKind);
        Assert.Equal("•Item", paragraph.Text);
    }
}
