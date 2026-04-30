using System.Net.Http;
using System.Text.Json;

namespace Novalist.Core.Services;

/// <summary>
/// Provides grammar and spelling checking via the LanguageTool API.
/// Uses the public API by default; supports self-hosted instances.
/// </summary>
public sealed class GrammarCheckService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private string _apiUrl = "https://api.languagetool.org/v2/check";

    /// <summary>
    /// Gets or sets the LanguageTool API endpoint URL.
    /// Defaults to the free public API.
    /// </summary>
    public string ApiUrl
    {
        get => _apiUrl;
        set => _apiUrl = string.IsNullOrWhiteSpace(value)
            ? "https://api.languagetool.org/v2/check"
            : value.TrimEnd('/');
    }

    /// <summary>
    /// Checks the given plain text for grammar and spelling issues.
    /// </summary>
    /// <param name="text">The plain text to check.</param>
    /// <param name="language">The LanguageTool language code (e.g. "en-US", "de-DE").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of grammar issues found in the text.</returns>
    public async Task<List<GrammarIssue>> CheckAsync(string text, string language, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["text"] = text,
            ["language"] = language,
            ["enabledOnly"] = "false"
        });

        HttpResponseMessage response;
        try
        {
            response = await Http.PostAsync(_apiUrl, content, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return [];
        }

        if (!response.IsSuccessStatusCode)
            return [];

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseResponse(json);
    }

    private static List<GrammarIssue> ParseResponse(string json)
    {
        var issues = new List<GrammarIssue>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("matches", out var matches))
                return issues;

            foreach (var match in matches.EnumerateArray())
            {
                var message = match.GetProperty("message").GetString() ?? string.Empty;
                var offset = match.GetProperty("offset").GetInt32();
                var length = match.GetProperty("length").GetInt32();

                var categoryId = string.Empty;
                if (match.TryGetProperty("rule", out var rule) &&
                    rule.TryGetProperty("category", out var category) &&
                    category.TryGetProperty("id", out var catId))
                {
                    categoryId = catId.GetString() ?? string.Empty;
                }

                var issueType = categoryId switch
                {
                    "TYPOS" or "SPELLING" => GrammarIssueType.Spelling,
                    "STYLE" or "REDUNDANCY" or "TYPOGRAPHY" => GrammarIssueType.Style,
                    _ => GrammarIssueType.Grammar
                };

                var replacements = new List<string>();
                if (match.TryGetProperty("replacements", out var reps))
                {
                    foreach (var rep in reps.EnumerateArray())
                    {
                        if (rep.TryGetProperty("value", out var val))
                        {
                            var replacement = val.GetString();
                            if (!string.IsNullOrEmpty(replacement))
                            {
                                replacements.Add(replacement);
                                if (replacements.Count >= 5) break; // Limit suggestions
                            }
                        }
                    }
                }

                issues.Add(new GrammarIssue
                {
                    Message = message,
                    Offset = offset,
                    Length = length,
                    Type = issueType,
                    Replacements = replacements
                });
            }
        }
        catch (JsonException)
        {
            // Malformed response — return empty
        }

        return issues;
    }

    /// <summary>
    /// Maps an application language code to a LanguageTool language code.
    /// </summary>
    public static string MapLanguageCode(string appLanguage)
    {
        return appLanguage switch
        {
            "en" => "en-US",
            "de" or "de-low" or "de-guillemet" => "de-DE",
            "fr" => "fr",
            "es" => "es",
            "pt" => "pt-BR",
            "it" => "it",
            "nl" => "nl",
            "pl" => "pl-PL",
            "ru" => "ru-RU",
            "uk" => "uk-UA",
            "ja" => "ja-JP",
            "zh" => "zh-CN",
            _ => "en-US"
        };
    }
}

/// <summary>
/// Represents a single grammar or spelling issue found in text.
/// </summary>
public sealed class GrammarIssue
{
    public string Message { get; init; } = string.Empty;
    public int Offset { get; init; }
    public int Length { get; init; }
    public GrammarIssueType Type { get; init; }
    public List<string> Replacements { get; init; } = [];
}

/// <summary>
/// Categorizes grammar issues for visual styling.
/// </summary>
public enum GrammarIssueType
{
    Spelling,
    Grammar,
    Style
}
