using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public class AiService : IAiService
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromMinutes(10) };

    private string _provider = "lmstudio";
    private string _baseUrl = "http://localhost:1234";
    private string _model = string.Empty;
    private string _apiToken = string.Empty;
    private double _temperature = 0.7;
    private int _contextLength;
    private double _topP = 0.9;
    private double _minP = 0.05;
    private double _frequencyPenalty = 1.1;
    private int _repeatLastN = 64;
    private string _systemPrompt = string.Empty;
    private readonly CopilotAcpClient _copilotClient = new();

    /// <summary>Language name for prompts (e.g. "English", "German").</summary>
    public string LanguageName { get; set; } = "English";

    private CancellationTokenSource? _cts;

    private bool IsCopilot => _provider == "copilot";

    public void Configure(AiSettings settings)
    {
        _provider = settings.Provider;
        _baseUrl = settings.LmStudioBaseUrl.TrimEnd('/');
        _model = settings.LmStudioModel;
        _apiToken = settings.LmStudioApiToken;
        _temperature = settings.Temperature;
        _contextLength = settings.ContextLength;
        _topP = settings.TopP;
        _minP = settings.MinP;
        _frequencyPenalty = settings.FrequencyPenalty;
        _repeatLastN = settings.RepeatLastN;
        _systemPrompt = settings.SystemPrompt;
        _copilotClient.ExecPath = settings.CopilotPath;
        _copilotClient.ModelId = settings.CopilotModel;
    }

    public void Cancel()
    {
        if (IsCopilot)
            _copilotClient.CancelPrompt();
        _cts?.Cancel();
    }

    // ── Server interaction ──────────────────────────────────────────

    public async Task<bool> IsServerRunningAsync()
    {
        if (IsCopilot)
            return await _copilotClient.IsAvailableAsync().ConfigureAwait(false);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/v1/models");
            AddAuth(req);
            using var res = await SharedClient.SendAsync(req).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<AiModelInfo>> ListModelsAsync()
    {
        if (IsCopilot)
        {
            var copilotModels = await _copilotClient.ListModelsAsync().ConfigureAwait(false);
            return copilotModels.Select(m => new AiModelInfo
            {
                Key = m.Id,
                DisplayName = m.Name,
                SizeBytes = 0,
            }).ToList();
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/v1/models");
            AddAuth(req);
            using var res = await SharedClient.SendAsync(req).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var models = new List<AiModelInfo>();
            if (doc.RootElement.TryGetProperty("models", out var arr))
            {
                foreach (var m in arr.EnumerateArray())
                {
                    var type = m.TryGetProperty("type", out var t) ? t.GetString() : null;
                    if (type != "llm") continue;
                    models.Add(new AiModelInfo
                    {
                        Key = m.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "",
                        DisplayName = m.TryGetProperty("display_name", out var dn) ? dn.GetString() ?? "" : "",
                        SizeBytes = m.TryGetProperty("size_bytes", out var sb) ? sb.GetInt64() : 0,
                    });
                }
            }
            return models;
        }
        catch
        {
            return [];
        }
    }

    public async Task EnsureModelLoadedAsync()
    {
        if (string.IsNullOrEmpty(_model)) return;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/v1/models");
            AddAuth(req);
            using var res = await SharedClient.SendAsync(req).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            bool needsLoad = true;
            if (doc.RootElement.TryGetProperty("models", out var arr))
            {
                foreach (var m in arr.EnumerateArray())
                {
                    var key = m.TryGetProperty("key", out var k) ? k.GetString() : null;
                    if (key != _model) continue;
                    if (m.TryGetProperty("loaded_instances", out var instances) && instances.GetArrayLength() > 0)
                    {
                        if (_contextLength <= 0)
                        {
                            needsLoad = false;
                        }
                        else
                        {
                            foreach (var inst in instances.EnumerateArray())
                            {
                                if (inst.TryGetProperty("config", out var cfg) &&
                                    cfg.TryGetProperty("context_length", out var cl) &&
                                    cl.GetInt32() == _contextLength)
                                {
                                    needsLoad = false;
                                    break;
                                }
                            }
                            if (needsLoad)
                            {
                                // Unload existing instances first
                                foreach (var inst in instances.EnumerateArray())
                                {
                                    var instId = inst.TryGetProperty("id", out var iid) ? iid.GetString() ?? _model : _model;
                                    await UnloadInstanceAsync(instId).ConfigureAwait(false);
                                }
                            }
                        }
                    }
                    break;
                }
            }

            if (needsLoad)
                await LoadModelAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort — proceed even if we can't verify load state
        }
    }

    private async Task LoadModelAsync()
    {
        var payload = new Dictionary<string, object> { ["model"] = _model };
        if (_contextLength > 0)
            payload["context_length"] = _contextLength;

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/v1/models/load");
        AddAuth(req);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var res = await SharedClient.SendAsync(req).ConfigureAwait(false);
    }

    private async Task UnloadInstanceAsync(string instanceId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/v1/models/unload");
        AddAuth(req);
        req.Content = new StringContent(
            JsonSerializer.Serialize(new { instance_id = instanceId }),
            Encoding.UTF8, "application/json");
        using var res = await SharedClient.SendAsync(req).ConfigureAwait(false);
    }

    // ── Chat generation with SSE streaming ──────────────────────────

    public async Task<AiChatResult> GenerateChatAsync(
        List<AiChatMessage> messages,
        Action<string>? onChunk = null,
        double? temperature = null,
        Action<string>? onThinkingChunk = null,
        CancellationToken cancellationToken = default)
    {
        if (IsCopilot)
            return await GenerateChatCopilotAsync(messages, onChunk, onThinkingChunk, cancellationToken);

        await EnsureModelLoadedAsync().ConfigureAwait(false);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _cts.Token;

        var body = new Dictionary<string, object>
        {
            ["model"] = _model,
            ["messages"] = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            ["stream"] = true,
            ["temperature"] = temperature ?? _temperature,
            ["top_p"] = _topP,
            ["min_p"] = _minP,
            ["frequency_penalty"] = _frequencyPenalty,
            ["repeat_last_n"] = _repeatLastN,
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/chat/completions");
        AddAuth(req);
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var res = await SharedClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();

        using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        var fullText = new StringBuilder();
        var thinkingText = new StringBuilder();
        var buffer = new StringBuilder();
        bool insideThinkTag = false;
        var tagBuffer = new StringBuilder();
        string? finishReason = null;

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line == null) break;

            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (trimmed == "data: [DONE]") break;
            if (!trimmed.StartsWith("data: ")) continue;

            var jsonStr = trimmed[6..];
            try
            {
                using var doc = JsonDocument.Parse(jsonStr);
                var root = doc.RootElement;

                // Check for error
                if (root.TryGetProperty("error", out var errObj))
                {
                    var errMsg = errObj.TryGetProperty("message", out var em) ? em.GetString() : jsonStr;
                    throw new InvalidOperationException($"LM Studio: {errMsg}");
                }

                if (!root.TryGetProperty("choices", out var choices)) continue;
                var first = choices.EnumerateArray().FirstOrDefault();
                if (!first.TryGetProperty("delta", out var delta)) continue;

                // Capture finish_reason to detect truncation
                if (first.TryGetProperty("finish_reason", out var frEl) && frEl.ValueKind == JsonValueKind.String)
                    finishReason = frEl.GetString();

                // Reasoning content (dedicated field)
                if (delta.TryGetProperty("reasoning_content", out var rc))
                {
                    var reasoning = rc.GetString() ?? "";
                    if (reasoning.Length > 0)
                    {
                        thinkingText.Append(reasoning);
                        onThinkingChunk?.Invoke(reasoning);
                    }
                }

                // Content with inline <think> tag handling
                if (delta.TryGetProperty("content", out var contentEl))
                {
                    var token = contentEl.GetString() ?? "";
                    if (token.Length > 0)
                    {
                        ProcessToken(token, ref insideThinkTag, tagBuffer, fullText, thinkingText, onChunk, onThinkingChunk);
                    }
                }
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"[AiService] SSE JSON parse error: {ex.Message}\n  Input: {jsonStr[..Math.Min(jsonStr.Length, 500)]}");
            }
        }

        // Flush tag buffer
        if (tagBuffer.Length > 0)
        {
            var remaining = tagBuffer.ToString();
            tagBuffer.Clear();
            if (insideThinkTag)
            {
                thinkingText.Append(remaining);
                onThinkingChunk?.Invoke(remaining);
            }
            else
            {
                fullText.Append(remaining);
                onChunk?.Invoke(remaining);
            }
        }

        _cts = null;
        var wasTruncated = finishReason == "length";
        if (wasTruncated)
            Debug.WriteLine("[AiService] WARNING: Response was truncated (finish_reason=length). Model hit token limit.");
        Debug.WriteLine($"[AiService] GenerateChat finished. finish_reason={finishReason}, response length={fullText.Length}, thinking length={thinkingText.Length}");
        return new AiChatResult { Response = fullText.ToString(), Thinking = thinkingText.ToString(), WasTruncated = wasTruncated };
    }

    /// <summary>Routes tokens through inline think-tag detection.</summary>
    private static void ProcessToken(
        string token, ref bool insideThinkTag, StringBuilder tagBuffer,
        StringBuilder fullText, StringBuilder thinkingText,
        Action<string>? onChunk, Action<string>? onThinkingChunk)
    {
        var pending = tagBuffer.ToString() + token;
        tagBuffer.Clear();

        while (pending.Length > 0)
        {
            if (insideThinkTag)
            {
                var closeIdx = pending.IndexOf("</think>", StringComparison.Ordinal);
                if (closeIdx >= 0)
                {
                    var chunk = pending[..closeIdx];
                    if (chunk.Length > 0)
                    {
                        thinkingText.Append(chunk);
                        onThinkingChunk?.Invoke(chunk);
                    }
                    insideThinkTag = false;
                    pending = pending[(closeIdx + 8)..];
                }
                else if (pending.Contains("</") && pending.Length < 8)
                {
                    tagBuffer.Append(pending);
                    pending = "";
                }
                else
                {
                    thinkingText.Append(pending);
                    onThinkingChunk?.Invoke(pending);
                    pending = "";
                }
            }
            else
            {
                var openIdx = pending.IndexOf("<think>", StringComparison.Ordinal);
                if (openIdx >= 0)
                {
                    var chunk = pending[..openIdx];
                    if (chunk.Length > 0)
                    {
                        fullText.Append(chunk);
                        onChunk?.Invoke(chunk);
                    }
                    insideThinkTag = true;
                    pending = pending[(openIdx + 7)..];
                }
                else if (pending.EndsWith('<') || (pending.Length < 7 && pending.Contains('<')))
                {
                    var ltIdx = pending.LastIndexOf('<');
                    var before = pending[..ltIdx];
                    if (before.Length > 0)
                    {
                        fullText.Append(before);
                        onChunk?.Invoke(before);
                    }
                    tagBuffer.Append(pending[ltIdx..]);
                    pending = "";
                }
                else
                {
                    fullText.Append(pending);
                    onChunk?.Invoke(pending);
                    pending = "";
                }
            }
        }
    }

    /// <summary>Generate chat via Copilot ACP — flatten messages and stream.</summary>
    private async Task<AiChatResult> GenerateChatCopilotAsync(
        List<AiChatMessage> messages,
        Action<string>? onChunk,
        Action<string>? onThinkingChunk,
        CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _copilotClient.OnChunk = onChunk;
        _copilotClient.OnThinkingChunk = onThinkingChunk;

        // Copilot ACP doesn't support system messages natively — flatten
        var flat = string.Join("\n\n",
            messages.Select(m => m.Role == "system" ? $"[System]\n{m.Content}" : m.Content));

        try
        {
            var response = await _copilotClient.GenerateAsync(flat);
            return new AiChatResult { Response = response, Thinking = string.Empty };
        }
        finally
        {
            _copilotClient.OnChunk = null;
            _copilotClient.OnThinkingChunk = null;
            _cts = null;
        }
    }

    /// <summary>Reset the Copilot ACP session (clears server-side history). No-op for LM Studio.</summary>
    public async Task ResetChatSessionAsync()
    {
        if (IsCopilot)
            await _copilotClient.ResetSessionAsync();
    }

    // ── Analysis methods ────────────────────────────────────────────

    public async Task<AiAnalysisResult> AnalyseChapterWholeAsync(
        string chapterText,
        List<EntitySummary> entities,
        List<string>? alreadyFound = null,
        ChapterContext? context = null,
        EnabledChecks? checks = null,
        Action<string>? onResponseChunk = null,
        Action<string>? onThinkingChunk = null,
        bool findAllReferences = false,
        CancellationToken cancellationToken = default)
    {
        checks ??= new EnabledChecks();
        if (!checks.References && !checks.Inconsistencies && !checks.Suggestions && !checks.SceneStats)
            return new AiAnalysisResult();

        var prompt = BuildChapterPrompt(chapterText, entities, alreadyFound, context, checks, findAllReferences);

        var messages = new List<AiChatMessage>();
        if (!string.IsNullOrEmpty(_systemPrompt))
            messages.Add(new AiChatMessage { Role = "system", Content = _systemPrompt });
        messages.Add(new AiChatMessage { Role = "user", Content = prompt });

        var result = await GenerateChatAsync(messages, onResponseChunk, 0, onThinkingChunk, cancellationToken);

        if (result.WasTruncated)
            Debug.WriteLine("[AiService] AnalyseChapterWhole: Response was truncated — JSON may be incomplete.");

        // Fallback: some models emit JSON inside thinking block
        var findings = ParseFindings(result.Response);
        if (findings.Count == 0 && !string.IsNullOrWhiteSpace(result.Thinking))
        {
            var thinkingFindings = ParseFindings(result.Thinking);
            if (thinkingFindings.Count > 0)
                findings = thinkingFindings;
        }

        return new AiAnalysisResult
        {
            Findings = findings,
            RawResponse = result.Response,
            Thinking = result.Thinking,
        };
    }

    public async Task<AiAnalysisResult> AnalyseWholeStoryAsync(
        List<ChapterTextEntry> chapters,
        List<EntitySummary> entities,
        List<ChapterFindingsEntry> cachedFindings,
        Action<string>? onResponseChunk = null,
        Action<string>? onThinkingChunk = null,
        CancellationToken cancellationToken = default)
    {
        var prompt = BuildWholeStoryPrompt(chapters, entities, cachedFindings);

        var messages = new List<AiChatMessage>();
        if (!string.IsNullOrEmpty(_systemPrompt))
            messages.Add(new AiChatMessage { Role = "system", Content = _systemPrompt });
        messages.Add(new AiChatMessage { Role = "user", Content = prompt });

        var result = await GenerateChatAsync(messages, onResponseChunk, 0, onThinkingChunk, cancellationToken);

        var findings = ParseFindings(result.Response);
        if (findings.Count == 0 && !string.IsNullOrWhiteSpace(result.Thinking))
        {
            var thinkingFindings = ParseFindings(result.Thinking);
            if (thinkingFindings.Count > 0)
                findings = thinkingFindings;
        }

        return new AiAnalysisResult
        {
            Findings = findings,
            RawResponse = result.Response,
            Thinking = result.Thinking,
        };
    }

    // ── Prompt building ─────────────────────────────────────────────

    private string BuildChapterPrompt(
        string chapterText, List<EntitySummary> entities,
        List<string>? alreadyFound, ChapterContext? context,
        EnabledChecks checks, bool findAllReferences)
    {
        var entityBlock = entities.Count > 0
            ? string.Join("\n", entities.Select(e => $"- [{e.Type}] {e.Name}: {e.Details}"))
            : "(no entities registered yet)";

        var alreadyFoundBlock = "";
        if (!findAllReferences && alreadyFound is { Count: > 0 })
        {
            alreadyFoundBlock = $"\nEntities already detected by regex matching (DO NOT report these as basic name-match references — only report them if you find an INDIRECT reference such as a pronoun, nickname, relationship term, or abbreviated name that the regex cannot catch):\n{string.Join(", ", alreadyFound)}\n";
        }

        var contextBlock = "";
        if (context != null)
        {
            var parts = new List<string> { $"Chapter \"{context.ChapterName}\"" };
            if (!string.IsNullOrEmpty(context.ActName)) parts.Add($"Act \"{context.ActName}\"");
            if (!string.IsNullOrEmpty(context.SceneName)) parts.Add($"Scene \"{context.SceneName}\"");
            if (!string.IsNullOrEmpty(context.Date)) parts.Add($"In-story date: {context.Date}");
            contextBlock = $"\nChapter context: {string.Join(", ", parts)}. The entity details above already reflect any act/chapter/scene-specific overrides.\n";
        }

        var tasks = BuildTaskInstructions(checks, alreadyFound, findAllReferences);
        var isScene = !string.IsNullOrEmpty(context?.SceneName);
        var textLabel = isScene ? "Scene text" : "Full chapter text";
        var scopeDescription = isScene
            ? "a scene from a novel chapter. The project tracks entities (characters, locations, items, lore) by matching their names as plain text — no special markup is used. You have the full scene text, so you can detect cross-paragraph patterns and narrative-level inconsistencies within this scene."
            : "a complete chapter from a novel. The project tracks entities (characters, locations, items, lore) by matching their names as plain text — no special markup is used. You have the full chapter text, so you can detect cross-paragraph patterns and narrative-level inconsistencies.";

        var typeEnum = checks.SceneStats
            ? "\"reference\", \"inconsistency\", \"suggestion\", or \"scene_stats\""
            : "\"reference\", \"inconsistency\", or \"suggestion\"";

        var sceneStatsFields = checks.SceneStats
            ? "\n\nFor objects with \"type\":\"scene_stats\", include these ADDITIONAL fields:\n- \"scenePov\": POV character name (string)\n- \"sceneEmotion\": one of \"neutral\",\"tense\",\"joyful\",\"melancholic\",\"angry\",\"fearful\",\"romantic\",\"mysterious\",\"humorous\",\"hopeful\",\"desperate\",\"peaceful\",\"chaotic\",\"sorrowful\",\"triumphant\"\n- \"sceneIntensity\": integer from -10 to +10\n- \"sceneConflict\": one-line conflict summary (string)"
            : "";

        var scope = isScene ? "scene" : "chapter";

        return $""""
            You are a fiction-writing assistant analysing {scopeDescription}

            IMPORTANT: Write all "title" and "description" values in {LanguageName}. The JSON keys and "type" / "entityType" enum values must remain in English.

            Known entities (note: relationship fields tell you who is connected — e.g. if John Doe has "Wife: Jane Doe", then "his wife" refers to Jane Doe):
            {entityBlock}
            {alreadyFoundBlock}{contextBlock}
            {textLabel}:
            """
            {chapterText}
            """

            Perform the following task(s) and return ONLY a JSON array (no markdown fences, no explanation outside the array). Each element must be an object with these fields:
            - "type": one of {typeEnum}
            - "title": short heading (max 80 chars)
            - "description": concise explanation
            - "excerpt": the EXACT text from the {scope} that this finding refers to (verbatim copy, max 120 chars). This will be used to locate the finding in the document.
            - "entityName": the entity name this relates to (or empty string)
            - "entityType": "character", "location", "item", "lore", or empty string{sceneStatsFields}

            {string.Join("\n", tasks)}

            If a task has no findings, simply omit entries for it. Return an empty array [] if nothing is found.
            """";
    }

    private string BuildWholeStoryPrompt(
        List<ChapterTextEntry> chapters, List<EntitySummary> entities,
        List<ChapterFindingsEntry> cachedFindings)
    {
        var entityBlock = entities.Count > 0
            ? string.Join("\n", entities.Select(e => $"- [{e.Type}] {e.Name}: {e.Details}"))
            : "(no entities registered yet)";

        var cachedBlock = "";
        if (cachedFindings.Any(cf => cf.Findings.Count > 0))
        {
            var lines = new List<string>();
            foreach (var cf in cachedFindings.Where(cf => cf.Findings.Count > 0))
            {
                lines.Add($"Chapter \"{cf.ChapterName}\":");
                foreach (var f in cf.Findings)
                    lines.Add($"  [{f.Type}] {f.Title}: {f.Description}{(!string.IsNullOrEmpty(f.Excerpt) ? $" (excerpt: \"{f.Excerpt}\")" : "")}");
            }
            cachedBlock = string.Join("\n", lines);
        }

        var storyBlock = string.Join("\n\n", chapters.Select(ch => $"--- Chapter: {ch.Name} ---\n{ch.Text}"));

        return $"""
            You are a fiction-analysis assistant performing a WHOLE-STORY review of a complete novel manuscript spanning {chapters.Count} chapter(s).

            IMPORTANT: Write all "title" and "description" values in {LanguageName}. The JSON keys and "type" / "entityType" enum values must remain in English.

            ## Known Entities
            {entityBlock}

            {(string.IsNullOrEmpty(cachedBlock) ? "" : $"""
            ## Preliminary Per-Chapter Findings (to be verified and cross-referenced)
            The following findings were discovered during previous per-chapter analysis. Treat them as preliminary hints that may contain duplicates, chapter-isolated false positives, or issues worth verifying across the full narrative:
            {cachedBlock}

            """)}## Full Story Text
            {storyBlock}

            ## Your Task
            Review the complete story and the entity data above and return a final, comprehensive list of findings. Focus on:

            1. **Inconsistencies** ("type":"inconsistency"): Contradictions that span the full story — e.g. a character's appearance described differently across chapters, location descriptions that contradict each other, timeline contradictions, or entity details that contradict their registered profiles.

            2. **Suggestions** ("type":"suggestion"): Character names, place names, or other notable entities mentioned in the story that are NOT yet registered in the entity list and should be added.

            Return ONLY a JSON array (no markdown fences, no explanation outside the array). Each element must have:
            - "type": "inconsistency" or "suggestion"
            - "title": short heading (max 80 chars)
            - "description": detailed explanation of the finding
            - "excerpt": best verbatim text excerpt from the story that illustrates the finding (max 120 chars)
            - "entityName": the entity name this relates to (or empty string)
            - "entityType": "character", "location", "item", "lore", or empty string

            Consolidate duplicate or overlapping findings into single entries. Return an empty array [] if nothing is found.

            IMPORTANT: After your thinking/reasoning, you MUST produce the JSON array as plain text in your response — not inside any thinking, reasoning, or code block tags. The first non-whitespace character of your final response must be "[".
            """;
    }

    private List<string> BuildTaskInstructions(EnabledChecks checks, List<string>? alreadyFound, bool findAllReferences)
    {
        var tasks = new List<string>();
        var taskNum = 1;

        if (checks.References)
        {
            var presenceRule = " IMPORTANT: Only report entities that are physically PRESENT in the scene or actively participating in the action. Do NOT report entities that are merely talked about, remembered, or referenced in dialogue but are not actually there.";
            if (findAllReferences)
                tasks.Add($"{taskNum}. **References** (\"type\":\"reference\"): Find ALL known entities that are physically present or actively participating in this scene — both direct name mentions and indirect references (relationship terms like \"his wife\", pronouns that resolve to a specific entity, nicknames, abbreviated names).{presenceRule} For each reference, set entityName to the full entity name and entityType.");
            else
            {
                var alreadyBlock = alreadyFound is { Count: > 0 } ? $" The regex system has already found: {string.Join(", ", alreadyFound)}. Only report references the regex missed." : "";
                tasks.Add($"{taskNum}. **References** (\"type\":\"reference\"): Find places where a known entity that is physically present in the scene is referenced INDIRECTLY — through relationship terms, pronouns, nicknames, or abbreviated names. Direct name mentions that simple regex matching would catch should NOT be reported.{alreadyBlock}{presenceRule} For each reference, set entityName to the full entity name.");
            }
            taskNum++;
        }

        if (checks.Inconsistencies)
        {
            tasks.Add($"{taskNum}. **Inconsistencies** (\"type\":\"inconsistency\"): Compare the text against the known entity details. Report any contradictions.");
            taskNum++;
        }

        if (checks.Suggestions)
        {
            tasks.Add($"{taskNum}. **Suggestions** (\"type\":\"suggestion\"): Identify character names, place names, or notable objects mentioned in the text that do NOT match any known entity. For every suggestion set \"entityName\" and \"entityType\".");
            taskNum++;
        }

        if (checks.SceneStats)
        {
            tasks.Add($"{taskNum}. **Scene Stats** (\"type\":\"scene_stats\"): Return EXACTLY ONE object with: \"scenePov\" (POV character name), \"sceneEmotion\" (one of the emotion enum values), \"sceneIntensity\" (integer -10 to +10), \"sceneConflict\" (one-line summary).");
        }

        return tasks;
    }

    // ── JSON parsing ────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static List<AiFinding> ParseFindings(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];

        // Strip inline <think>…</think> blocks so we parse only the actual response
        var cleaned = Regex.Replace(raw, @"<think>[\s\S]*?</think>", "", RegexOptions.IgnoreCase).Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
            cleaned = raw.Trim(); // fall back to original if stripping removed everything

        // Strip markdown code fences
        cleaned = Regex.Replace(cleaned, @"```(?:json)?\s*", "").TrimEnd();

        Debug.WriteLine($"[AiService] ParseFindings: cleaned length={cleaned.Length}");

        // Try each JSON array candidate (models sometimes emit a preview like [{"type":"...", ...}] before the real array)
        var matches = Regex.Matches(cleaned, @"\[\s*\{");
        foreach (Match m in matches)
        {
            var startIdx = m.Index;

            // Find the matching closing "]" using brace-depth tracking
            int depth = 0;
            bool inStr = false;
            char prev = '\0';
            var endIdx = -1;
            for (int i = startIdx; i < cleaned.Length; i++)
            {
                var c = cleaned[i];
                if (inStr)
                {
                    if (c == '"' && prev != '\\') inStr = false;
                }
                else
                {
                    if (c == '"') inStr = true;
                    else if (c == '[') depth++;
                    else if (c == ']')
                    {
                        depth--;
                        if (depth == 0) { endIdx = i; break; }
                    }
                }
                prev = c;
            }

            if (endIdx < 0 || endIdx <= startIdx) continue;

            var jsonStr = cleaned[startIdx..(endIdx + 1)];

            // Skip candidates containing literal ellipsis placeholders (e.g. "...", ...)
            if (Regex.IsMatch(jsonStr, @"""\.\.\.""") || Regex.IsMatch(jsonStr, @",\s*\.\.\."))
            {
                Debug.WriteLine($"[AiService] ParseFindings: skipping ellipsis-placeholder candidate at {startIdx}..{endIdx}");
                continue;
            }

            Debug.WriteLine($"[AiService] ParseFindings: trying candidate at {startIdx}..{endIdx} (length={jsonStr.Length})");

            List<AiFinding> items;
            try
            {
                items = JsonSerializer.Deserialize<List<AiFinding>>(jsonStr, JsonOpts) ?? [];
            }
            catch (JsonException)
            {
                items = ExtractJsonObjects(jsonStr);
            }

            var valid = items
                .Where(f => !string.IsNullOrEmpty(f.Type) && !string.IsNullOrEmpty(f.Title))
                .ToList();

            if (valid.Count > 0)
            {
                Debug.WriteLine($"[AiService] ParseFindings: matched candidate at {startIdx}, {valid.Count} findings");
                return valid.Select(NormalizeFinding).ToList();
            }
        }

        // Fallback: try extracting individual JSON objects (model may have returned objects without array wrapper)
        Debug.WriteLine("[AiService] ParseFindings: no array candidate found, trying individual object extraction.");
        var looseObjects = ExtractJsonObjects(cleaned);
        var looseValid = looseObjects
            .Where(f => !string.IsNullOrEmpty(f.Type) && !string.IsNullOrEmpty(f.Title))
            .ToList();
        if (looseValid.Count > 0)
        {
            Debug.WriteLine($"[AiService] ParseFindings: found {looseValid.Count} findings via loose object extraction.");
            return looseValid.Select(NormalizeFinding).ToList();
        }

        Debug.WriteLine("[AiService] ParseFindings: No valid findings found.");
        return [];
    }

    private static AiFinding NormalizeFinding(AiFinding f)
    {
        if (f.Type == "suggestion" && string.IsNullOrEmpty(f.EntityName) && !string.IsNullOrEmpty(f.Title))
        {
            var asMatch = Regex.Match(f.Title, @"^(.+?)\s+as\s+", RegexOptions.IgnoreCase);
            f.EntityName = asMatch.Success ? asMatch.Groups[1].Value.Trim() : f.Title;
            if (string.IsNullOrEmpty(f.EntityType))
                f.EntityType = "item";
        }
        return f;
    }

    private static List<AiFinding> ExtractJsonObjects(string arrayStr)
    {
        var results = new List<AiFinding>();
        int depth = 0, start = -1;
        bool inString = false;
        char prevChar = '\0';

        for (int i = 0; i < arrayStr.Length; i++)
        {
            var ch = arrayStr[i];
            if (inString)
            {
                if (ch == '"' && prevChar != '\\') inString = false;
            }
            else
            {
                if (ch == '"') inString = true;
                else if (ch == '{') { if (depth == 0) start = i; depth++; }
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        var objStr = arrayStr[start..(i + 1)];
                        var parsed = TryParseObject(objStr);
                        if (parsed != null)
                            results.Add(parsed);
                        start = -1;
                    }
                }
            }
            prevChar = ch;
        }
        return results;
    }

    private static AiFinding? TryParseObject(string objStr)
    {
        try
        {
            return JsonSerializer.Deserialize<AiFinding>(objStr, JsonOpts);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AiService] TryParseObject: Direct parse failed: {ex.Message}\n  Input: {objStr[..Math.Min(objStr.Length, 500)]}");
            // Attempt repair of unescaped quotes
            var repaired = Regex.Replace(objStr,
                @"""(type|title|description|excerpt|entityName|entityType)""\s*:\s*""([\s\S]*?)(?:""\s*(?=[,}\]]))",
                m =>
                {
                    var key = m.Groups[1].Value;
                    var val = m.Groups[2].Value.Replace("\"", "\\\"");
                    return $"\"{key}\": \"{val}\"";
                });
            try { return JsonSerializer.Deserialize<AiFinding>(repaired, JsonOpts); }
            catch (Exception ex2)
            {
                Debug.WriteLine($"[AiService] TryParseObject: Repair parse also failed: {ex2.Message}\n  Repaired: {repaired[..Math.Min(repaired.Length, 500)]}");
                return null;
            }
        }
    }

    /// <summary>Strip inline &lt;think&gt;…&lt;/think&gt; blocks.</summary>
    public static (string Cleaned, string Thinking) StripInlineThinking(string text)
    {
        var thinkParts = new List<string>();
        var cleaned = Regex.Replace(text, @"<think>([\s\S]*?)</think>", m =>
        {
            var part = m.Groups[1].Value.Trim();
            if (part.Length > 0) thinkParts.Add(part);
            return "";
        });
        return (cleaned.Trim(), string.Join("\n\n", thinkParts));
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private void AddAuth(HttpRequestMessage req)
    {
        if (!string.IsNullOrEmpty(_apiToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
    }
}
