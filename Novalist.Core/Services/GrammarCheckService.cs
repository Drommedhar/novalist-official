using System.Net.Http;
using System.Text.Json;

namespace Novalist.Core.Services;

/// <summary>
/// Provides grammar and spelling checking via the LanguageTool API.
/// Uses the public API by default; supports self-hosted instances.
/// </summary>
public sealed class GrammarCheckService
{
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly HttpClient _http;
    private string _apiUrl = "https://api.languagetool.org/v2/check";

    /// <summary>
    /// Optional API key for LanguageTool Cloud (premium).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Optional username (email) for LanguageTool Cloud (premium).
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Whether advanced/picky checking style rules are enabled.
    /// </summary>
    public bool PickyMode { get; set; }

    /// <summary>
    /// Optional native/mother tongue language code (e.g. "de", "fr").
    /// </summary>
    public string? MotherTongue { get; set; }

    /// <summary>
    /// True when we've validated that the configured credentials are accepted by
    /// the LanguageTool Cloud endpoint and premium checks are available.
    /// </summary>
    public bool IsPremiumAvailable { get; private set; }

    private const string PublicApiDefault = "https://api.languagetool.org/v2/check";
    private const string PlusApiDefault = "https://api.languagetoolplus.com/v2/check";

    /// <param name="http">HTTP client to use; defaults to a shared long-lived client. Tests inject a fake-handler client.</param>
    public GrammarCheckService(HttpClient? http = null) => _http = http ?? SharedHttp;

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

        var formFields = new Dictionary<string, string>
        {
            ["text"] = text,
            ["language"] = language,
            ["enabledOnly"] = "false"
        };

        if (!string.IsNullOrWhiteSpace(ApiKey))
            formFields["apiKey"] = ApiKey;
        if (!string.IsNullOrWhiteSpace(Username))
            formFields["username"] = Username;
        if (PickyMode)
            formFields["level"] = "picky";
        if (!string.IsNullOrWhiteSpace(MotherTongue))
            formFields["motherTongue"] = MotherTongue;

        var content = new FormUrlEncodedContent(formFields);

        HttpResponseMessage response;
        var requestUrl = _apiUrl;
        if ((!string.IsNullOrWhiteSpace(ApiKey) || !string.IsNullOrWhiteSpace(Username)) &&
            string.Equals(_apiUrl, PublicApiDefault, StringComparison.OrdinalIgnoreCase))
        {
            requestUrl = PlusApiDefault;
        }

        System.Diagnostics.Debug.WriteLine($"[GrammarCheckService] Sending POST to {requestUrl} (ApiKey present: {!string.IsNullOrEmpty(ApiKey)})");
        var swHttp = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            response = await _http.PostAsync(requestUrl, content, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"[GrammarCheckService] POST to {requestUrl} threw {ex.GetType().Name} after {swHttp.ElapsedMilliseconds}ms");
            return [];
        }
        finally
        {
            swHttp.Stop();
        }

        System.Diagnostics.Debug.WriteLine($"[GrammarCheckService] POST to {requestUrl} completed with status {response.StatusCode} in {swHttp.ElapsedMilliseconds}ms");

        if (!response.IsSuccessStatusCode)
            return [];

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseResponse(json);
    }

    /// <summary>
    /// Attempts a lightweight validation of the configured credentials by issuing
    /// a small check request. Returns true when they are accepted (HTTP 200)
    /// and sets <see cref="IsPremiumAvailable"/>.
    /// </summary>
    public async Task<bool> ValidateCredentialsAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ApiKey) || string.IsNullOrWhiteSpace(Username))
        {
            IsPremiumAvailable = false;
            return false;
        }

        var requestUrl = _apiUrl;
        if (string.Equals(_apiUrl, PublicApiDefault, StringComparison.OrdinalIgnoreCase))
            requestUrl = PlusApiDefault;

        try
        {
            var form = new Dictionary<string, string>
            {
                ["text"] = "ping",
                ["language"] = "en-US",
                ["enabledOnly"] = "false",
                ["apiKey"] = ApiKey,
                ["username"] = Username
            };

            using var content = new FormUrlEncodedContent(form);
            var resp = await _http.PostAsync(requestUrl, content, cancellationToken).ConfigureAwait(false);
            IsPremiumAvailable = resp.IsSuccessStatusCode;
            return IsPremiumAvailable;
        }
        catch
        {
            IsPremiumAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// Adds a word to the user's personal dictionary.
    /// </summary>
    public async Task<bool> AddToDictionaryAsync(string word, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ApiKey) || string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(word))
            return false;

        var requestUrl = _apiUrl;
        if (string.Equals(_apiUrl, PublicApiDefault, StringComparison.OrdinalIgnoreCase))
            requestUrl = PlusApiDefault;

        if (requestUrl.EndsWith("/check", StringComparison.OrdinalIgnoreCase))
        {
            requestUrl = requestUrl.Substring(0, requestUrl.Length - 6) + "/words/add";
        }
        else
        {
            requestUrl = "https://api.languagetoolplus.com/v2/words/add";
        }

        try
        {
            var form = new Dictionary<string, string>
            {
                ["word"] = word.Trim(),
                ["apiKey"] = ApiKey,
                ["username"] = Username
            };

            using var content = new FormUrlEncodedContent(form);
            var resp = await _http.PostAsync(requestUrl, content, cancellationToken).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
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
