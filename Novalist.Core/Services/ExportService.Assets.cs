using System.Net;
using System.Text.RegularExpressions;
using Novalist.Core.Models;
using static Novalist.Core.Services.ExportProse;

namespace Novalist.Core.Services;

public partial class ExportService
{
    private Task<string> MaterializeAssetAsync(string path)
        => _materializeAsset == null || string.IsNullOrWhiteSpace(path)
            ? Task.FromResult(path) : _materializeAsset(path);

    private async Task<string> MaterializeImagesAsync(string html)
    {
        foreach (var image in ImageTagRegex().Matches(html).Reverse())
        {
            var attributes = image.Groups["attrs"];
            var source = ImageSourceRegex().Match(attributes.Value).Groups["value"];
            if (!source.Success || source.Length == 0) continue;
            var path = WebUtility.HtmlDecode(source.Value);
            if (!Path.IsPathFullyQualified(path)) continue;
            var local = await MaterializeAssetAsync(path);
            var offset = attributes.Index + source.Index;
            html = html.Remove(offset, source.Length).Insert(offset,
                WebUtility.HtmlEncode(local.Replace(Path.DirectorySeparatorChar, '/')));
        }
        return html;
    }

    private async Task<Dictionary<string, string>> MaterializeCodexImagesAsync(CodexContent content, ExportOptions options)
    {
        var images = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_entityService == null || !options.IncludesPart("images")) return images;
        var entities = content.Characters.Cast<IEntityData>().Concat(content.Locations).Concat(content.Items).Concat(content.Lore);
        foreach (var image in entities.SelectMany(entity => entity.Images ?? []).Where(image => !string.IsNullOrWhiteSpace(image.Path)))
        {
            if (images.ContainsKey(image.Path)) continue;
            images.Add(image.Path, await MaterializeAssetAsync(_entityService.GetImageFullPath(image.Path)));
        }
        return images;
    }

    [GeneratedRegex("(?:^|\\s)src\\s*=\\s*([\"'])(?<value>.*?)\\1", RegexOptions.IgnoreCase)]
    private static partial Regex ImageSourceRegex();
}
