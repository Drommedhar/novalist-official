using System.Collections.Concurrent;
using PdfSharp.Fonts;
using SkiaSharp;

namespace Novalist.Core.Services;

/// <summary>Uses the platform's installed fonts for PDF exports on every supported OS.</summary>
internal sealed class PdfFontResolver : IFontResolver
{
    private static readonly Lazy<PdfFontResolver> Registered = new(() =>
    {
        var resolver = new PdfFontResolver();
        GlobalFontSettings.FontResolver = resolver;
        return resolver;
    });

    private readonly ConcurrentDictionary<string, byte[]> _fonts = new(StringComparer.Ordinal);

    private readonly Func<string, SKFontStyle, SKTypeface?> _matchFamily;

    internal PdfFontResolver(Func<string, SKFontStyle, SKTypeface?>? matchFamily = null)
        => _matchFamily = matchFamily ?? ((family, style) => SKFontManager.Default.MatchFamily(family, style));

    internal static void EnsureInitialized() => _ = Registered.Value;

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        using var style = new SKFontStyle(isBold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal, isItalic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        string[] fallbacks = familyName.ToUpperInvariant() switch
        {
            "TIMES NEW ROMAN" => ["Liberation Serif", "DejaVu Serif"],
            "COURIER NEW" => ["Liberation Mono", "DejaVu Sans Mono"],
            _ => []
        };
        // MatchFamily returns null instead of silently choosing an Apple-only font
        // whose character and name tables PDFsharp cannot read.
        using var typeface = new[] { familyName }.Concat(fallbacks)
            .Concat(["Arial", "Liberation Sans", "DejaVu Sans"])
            .Select(family => _matchFamily(family, style)).FirstOrDefault(face => face is not null)
            ?? throw new InvalidOperationException("No compatible installed font is available for PDF export.");
        // CoreText-backed Skia streams already extract a single face from Apple font collections.
        using var stream = typeface.OpenStream();
        using var data = SKData.Create(stream);
        var faceName = $"{familyName}:{isBold}:{isItalic}";
        _fonts.TryAdd(faceName, data.ToArray());
        return new FontResolverInfo(faceName, isBold && !typeface.IsBold, isItalic && !typeface.IsItalic);
    }

    public byte[] GetFont(string faceName) => _fonts[faceName];
}
