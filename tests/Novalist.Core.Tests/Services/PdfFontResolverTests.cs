using Novalist.Core.Services;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SkiaSharp;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class PdfFontResolverTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void InstalledFontRetainsItsFamilyAndStyle(bool bold, bool italic)
    {
        var resolver = new PdfFontResolver();
        var family = InstalledFamily();
        var resolved = resolver.ResolveTypeface(family, bold, italic);

        using var data = SKData.CreateCopy(resolver.GetFont(resolved.FaceName));
        using var font = SKTypeface.FromData(data);

        Assert.NotNull(font);
        Assert.Equal(family, font.FamilyName, ignoreCase: true);
        Assert.Equal(bold, font.IsBold);
        Assert.Equal(italic, font.IsItalic);
        Assert.False(resolved.MustSimulateBold);
        Assert.False(resolved.MustSimulateItalic);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void MissingFontFallsBackToAnInstalledFaceWithTheRequestedStyle(bool bold, bool italic)
    {
        var resolver = new PdfFontResolver();
        var resolved = resolver.ResolveTypeface("Novalist Missing Font 9b62ce9c", bold, italic);

        using var data = SKData.CreateCopy(resolver.GetFont(resolved.FaceName));
        using var font = SKTypeface.FromData(data);

        Assert.NotNull(font);
        Assert.True(font.GlyphCount > 0);
        Assert.Contains(font.FamilyName, new[] { "Arial", "Liberation Sans", "DejaVu Sans" });
        Assert.Equal(bold, font.IsBold);
        Assert.Equal(italic, font.IsItalic);
    }

    [Theory]
    [InlineData("Times New Roman", "Liberation Serif")]
    [InlineData("Times New Roman", "DejaVu Serif")]
    [InlineData("times new roman", "Liberation Serif")]
    [InlineData("Courier New", "Liberation Mono")]
    [InlineData("Courier New", "DejaVu Sans Mono")]
    [InlineData("Missing Font", "Arial")]
    [InlineData("Missing Font", "Liberation Sans")]
    [InlineData("Missing Font", "DejaVu Sans")]
    public void MissingPrimaryFontUsesTheMatchingFallbackCategory(string requested, string available)
    {
        // Model an OS with only one compatible fallback installed, using real font bytes.
        var installedFamily = InstalledFamily();
        var resolver = new PdfFontResolver((family, style) => family == available
            ? SKTypeface.FromFamilyName(installedFamily, style) : null);
        var resolved = resolver.ResolveTypeface(requested, true, true);

        using var data = SKData.CreateCopy(resolver.GetFont(resolved.FaceName));
        using var font = SKTypeface.FromData(data);
        Assert.NotNull(font);
        Assert.Equal(installedFamily, font.FamilyName);
        Assert.True(font.IsBold);
        Assert.True(font.IsItalic);
    }

    [Fact]
    public void MissingAllFontsReportsThatExportCannotResolveAFont()
    {
        var resolver = new PdfFontResolver((_, _) => null);
        Assert.Throws<InvalidOperationException>(() =>
            resolver.ResolveTypeface("Times New Roman", false, false));
    }

    [Fact]
    public void RepeatedResolutionKeepsFontDataAvailable()
    {
        var resolver = new PdfFontResolver();
        var family = InstalledFamily();
        var first = resolver.ResolveTypeface(family, false, false);
        var bytes = resolver.GetFont(first.FaceName);
        var second = resolver.ResolveTypeface(family, false, false);

        Assert.Equal(first.FaceName, second.FaceName);
        Assert.Same(bytes, resolver.GetFont(second.FaceName));
    }

    [Fact]
    public async Task InitializationIsSafeToRepeatConcurrently()
    {
        await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => Task.Run(PdfFontResolver.EnsureInitialized)));
        var resolver = Assert.IsType<PdfFontResolver>(GlobalFontSettings.FontResolver);
        PdfFontResolver.EnsureInitialized();
        Assert.Same(resolver, GlobalFontSettings.FontResolver);

    }

    [Theory]
    [InlineData("Times New Roman")]
    [InlineData("Courier New")]
    [InlineData("Novalist Missing Font 9b62ce9c")]
    public void PdfEmbedsRegularBoldItalicAndBoldItalicFonts(string family)
    {
        PdfFontResolver.EnsureInitialized();
        using var document = new PdfDocument();
        var page = document.AddPage();
        using (var graphics = XGraphics.FromPdfPage(page))
        {
            foreach (var style in new[] { XFontStyleEx.Regular, XFontStyleEx.Bold,
                XFontStyleEx.Italic, XFontStyleEx.BoldItalic })
            {
                var font = new XFont(family, 12, style,
                    new XPdfFontOptions(PdfFontEncoding.Unicode));
                graphics.DrawString("Résumé — Čapek", font, XBrushes.Black,
                    new XPoint(30, 50 + 20 * (int)style));
            }
        }
        using var output = new MemoryStream();
        document.Save(output, false);
        output.Position = 0;
        using var reopened = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        var descriptors = reopened.Internals.GetAllObjects().OfType<PdfDictionary>()
            .Where(item => item.Elements.GetName("/Type") == "/FontDescriptor").ToList();
        Assert.Equal(4, descriptors.Count);
        Assert.All(descriptors, item =>
            Assert.True(item.Elements.GetDictionary("/FontFile2")?.Stream?.Value.Length > 0));
    }

    private static string InstalledFamily()
    {
        string[] candidates = ["Arial", "DejaVu Sans", "Liberation Sans", "Helvetica"];
        return SKFontManager.Default.FontFamilies.First(name =>
            candidates.Contains(name, StringComparer.OrdinalIgnoreCase));
    }
}
