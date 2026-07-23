using System.Text.RegularExpressions;
using System.Net;

namespace Novalist.Core.Services;

/// <summary>
/// Fetches the &lt;title&gt; of a web page so a pasted URL can become a readable
/// research entry instead of a bare address. Strictly on demand — nothing here
/// runs unless the writer asks for it, keeping the app offline-first. Failures
/// (offline, timeout, non-HTML, no title) return null rather than throwing, so
/// the caller simply keeps the URL as the title.
/// </summary>
public sealed partial class LinkTitleService
{
    private const int MaxBytes = 256 * 1024; // titles live in the <head>; don't read whole pages

    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly HttpClient _http;

    public LinkTitleService(HttpClient? http = null) => _http = http ?? SharedHttp;

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    /// <summary>Returns the page title, or null when it cannot be determined.</summary>
    public async Task<string?> FetchTitleAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;
        // Only ever speak HTTP(S) — never file:// or anything else a paste might carry.
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;

        try
        {
            using var response = await _http
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await ReadCappedAsync(response, cancellationToken).ConfigureAwait(false);
            var match = TitleRegex().Match(body);
            if (!match.Success)
                return null;

            var title = WebUtility.HtmlDecode(match.Groups[1].Value);
            title = WhitespaceRegex().Replace(title, " ").Trim();
            return title.Length == 0 ? null : title;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return null; // offline, timed out, or the connection dropped
        }
    }

    /// <summary>Reads at most <see cref="MaxBytes"/> of the response body.</summary>
    private static async Task<string> ReadCappedAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[MaxBytes];
        var total = 0;
        while (total < MaxBytes)
        {
            var read = await stream
                .ReadAsync(buffer.AsMemory(total, MaxBytes - total), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0) break;
            total += read;
        }
        return System.Text.Encoding.UTF8.GetString(buffer, 0, total);
    }
}
