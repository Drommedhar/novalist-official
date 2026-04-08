using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Novalist.Sdk.Services;

/// <summary>
/// Client for the GitHub Copilot CLI using the Agent Client Protocol (ACP)
/// over NDJSON/stdio transport.
/// </summary>
public class CopilotAcpClient : IDisposable
{
    private Process? _proc;
    private int _nextId;
    private string _buffer = string.Empty;
    private string? _sessionId;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private string _promptText = string.Empty;
    private string? _modelConfigId;
    private List<CopilotModelInfo> _cachedModels = [];

    /// <summary>Path to the Copilot CLI executable.</summary>
    public string ExecPath { get; set; } = "copilot";

    /// <summary>Working directory for sessions.</summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>Desired model id (empty = Copilot default).</summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>Callback invoked for each streamed text chunk.</summary>
    public Action<string>? OnChunk { get; set; }

    /// <summary>Callback invoked for each streamed thinking/reasoning chunk.</summary>
    public Action<string>? OnThinkingChunk { get; set; }

    /// <summary>Whether the process is alive.</summary>
    public bool IsAlive => _proc is { HasExited: false };

    /// <summary>Start the ACP process, initialize, and create a session.</summary>
    public async Task StartAsync()
    {
        if (IsAlive && _sessionId != null) return;

        await StopAsync();
        _sessionId = null;

        var psi = new ProcessStartInfo(ExecPath, "--acp --stdio")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (!string.IsNullOrEmpty(WorkingDirectory))
            psi.WorkingDirectory = WorkingDirectory;

        _proc = new Process { StartInfo = psi };
        _proc.OutputDataReceived += OnOutputData;
        _proc.ErrorDataReceived += OnErrorData;
        _proc.Exited += OnExited;
        _proc.EnableRaisingEvents = true;
        _proc.Start();
        _proc.BeginOutputReadLine();
        _proc.BeginErrorReadLine();

        // Initialize
        await RpcRequestAsync("initialize", new
        {
            protocolVersion = 1,
            clientCapabilities = new { },
            clientInfo = new
            {
                name = "novalist",
                title = "Novalist",
                version = "1.0.0",
            },
        });

        // Create session
        var sessionResult = await RpcRequestAsync("session/new", new
        {
            cwd = !string.IsNullOrEmpty(WorkingDirectory) ? WorkingDirectory : Environment.CurrentDirectory,
            mcpServers = Array.Empty<object>(),
        });

        _sessionId = sessionResult.GetProperty("sessionId").GetString();
        ParseModels(sessionResult);

        // Discover config option for model selector
        _modelConfigId = null;
        if (sessionResult.TryGetProperty("configOptions", out var opts))
        {
            foreach (var opt in opts.EnumerateArray())
            {
                if (opt.TryGetProperty("category", out var cat) && cat.GetString() == "model" &&
                    opt.TryGetProperty("id", out var id))
                {
                    _modelConfigId = id.GetString();
                    break;
                }
            }
        }

        if (!string.IsNullOrEmpty(ModelId))
            await SelectModelAsync(ModelId);
    }

    /// <summary>Stop the ACP process gracefully.</summary>
    public async Task StopAsync()
    {
        if (_proc == null) return;
        var p = _proc;
        _proc = null;
        _sessionId = null;

        try { p.StandardInput.Close(); } catch { /* ignore */ }
        try { p.Kill(); } catch { /* ignore */ }

        await Task.Run(() =>
        {
            try { p.WaitForExit(2000); } catch { /* ignore */ }
        });

        p.Dispose();
    }

    /// <summary>Reset the session to clear server-side conversation history.</summary>
    public async Task ResetSessionAsync()
    {
        if (!IsAlive || _sessionId == null) return;

        var sessionResult = await RpcRequestAsync("session/new", new
        {
            cwd = !string.IsNullOrEmpty(WorkingDirectory) ? WorkingDirectory : Environment.CurrentDirectory,
            mcpServers = Array.Empty<object>(),
        });

        _sessionId = sessionResult.GetProperty("sessionId").GetString();
        ParseModels(sessionResult);

        if (!string.IsNullOrEmpty(ModelId))
            await SelectModelAsync(ModelId);
    }

    /// <summary>Send cancel notification to abort current prompt.</summary>
    public void CancelPrompt()
    {
        if (_sessionId != null && _proc?.HasExited == false)
        {
            SendMessage(new { jsonrpc = "2.0", method = "session/cancel", @params = new { sessionId = _sessionId } });
        }
    }

    /// <summary>Send a text prompt and return the full response.</summary>
    public async Task<string> GenerateAsync(string prompt)
    {
        if (!IsAlive || _sessionId == null)
            await StartAsync();

        _promptText = string.Empty;

        var result = await RpcRequestAsync("session/prompt", new
        {
            sessionId = _sessionId,
            prompt = new[] { new { type = "text", text = prompt } },
        });

        var text = _promptText;
        _promptText = string.Empty;

        var stopReason = result.TryGetProperty("stopReason", out var sr) ? sr.GetString() : null;
        if (stopReason != "end_turn")
            throw new InvalidOperationException($"Copilot stopped with reason: {stopReason}");

        return text;
    }

    /// <summary>Check whether the Copilot CLI is reachable.</summary>
    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var psi = new ProcessStartInfo(ExecPath, "--help")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            using var cts = new CancellationTokenSource(5000);
            await proc.WaitForExitAsync(cts.Token);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>List available models (cached from last session/new).</summary>
    public async Task<List<CopilotModelInfo>> ListModelsAsync()
    {
        if (!IsAlive || _sessionId == null)
            await StartAsync();
        return _cachedModels;
    }

    /// <summary>Apply model selection to the running session.</summary>
    public async Task ApplyModelAsync(string modelId)
    {
        if (!IsAlive || _sessionId == null) return;
        await SelectModelAsync(modelId);
    }

    // ── Internal helpers ─────────────────────────────────────────

    private async Task SelectModelAsync(string modelId)
    {
        // Try session/set_config_option first (generic ACP)
        if (_modelConfigId != null)
        {
            try
            {
                await RpcRequestAsync("session/set_config_option", new
                {
                    sessionId = _sessionId,
                    configId = _modelConfigId,
                    value = modelId,
                });
                return;
            }
            catch { /* fall through */ }
        }

        // Try session/set_model (Copilot-specific)
        try
        {
            await RpcRequestAsync("session/set_model", new
            {
                sessionId = _sessionId,
                modelId,
            });
        }
        catch { /* best effort */ }
    }

    private void ParseModels(JsonElement result)
    {
        _cachedModels = [];
        if (result.TryGetProperty("models", out var models) &&
            models.TryGetProperty("availableModels", out var arr))
        {
            foreach (var m in arr.EnumerateArray())
            {
                var id = m.TryGetProperty("modelId", out var mid) ? mid.GetString() ?? "" : "";
                var name = m.TryGetProperty("name", out var mn) ? mn.GetString() ?? id : id;
                _cachedModels.Add(new CopilotModelInfo { Id = id, Name = name });
            }
        }
    }

    private void SendMessage(object msg)
    {
        if (_proc?.HasExited != false) return;
        try
        {
            var json = JsonSerializer.Serialize(msg);
            _proc.StandardInput.WriteLine(json);
            _proc.StandardInput.Flush();
        }
        catch { /* process may have died */ }
    }

    private Task<JsonElement> RpcRequestAsync(string method, object parameters)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        SendMessage(new { jsonrpc = "2.0", id, method, @params = parameters });

        // Timeout to avoid hanging forever
        _ = Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(_ =>
        {
            if (_pending.TryRemove(id, out var t))
                t.TrySetException(new TimeoutException($"ACP request '{method}' timed out"));
        });

        return tcs.Task;
    }

    private void OnOutputData(object sender, DataReceivedEventArgs e)
    {
        if (e.Data == null) return;
        ProcessLine(e.Data);
    }

    private void ProcessLine(string line)
    {
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement.Clone();
            HandleMessage(root);
        }
        catch { /* invalid JSON */ }
    }

    private void HandleMessage(JsonElement msg)
    {
        // Response to one of our requests
        if (msg.TryGetProperty("id", out var idProp))
        {
            var id = idProp.GetInt32();

            if (msg.TryGetProperty("error", out var err))
            {
                if (_pending.TryRemove(id, out var tcs))
                {
                    var message = err.TryGetProperty("message", out var m) ? m.GetString() ?? "ACP error" : "ACP error";
                    tcs.TrySetException(new InvalidOperationException(message));
                }
                return;
            }

            if (msg.TryGetProperty("result", out var result))
            {
                if (_pending.TryRemove(id, out var tcs))
                    tcs.TrySetResult(result.Clone());
                return;
            }

            // Incoming request from agent (e.g. permission request)
            if (msg.TryGetProperty("method", out var method))
            {
                HandleIncomingRequest(id, method.GetString() ?? "", msg);
                return;
            }
        }

        // Notification (no id, has method)
        if (msg.TryGetProperty("method", out var notifMethod) && !msg.TryGetProperty("id", out _))
        {
            HandleNotification(notifMethod.GetString() ?? "", msg);
        }
    }

    private void HandleIncomingRequest(int id, string method, JsonElement msg)
    {
        if (method == "session/request_permission")
        {
            // Auto-reject all permission requests — we only want text generation
            JsonElement? rejectOptId = null;
            if (msg.TryGetProperty("params", out var p) &&
                p.TryGetProperty("options", out var options))
            {
                foreach (var opt in options.EnumerateArray())
                {
                    var kind = opt.TryGetProperty("kind", out var k) ? k.GetString() : null;
                    if (kind is "reject_once" or "reject_always")
                    {
                        if (opt.TryGetProperty("optionId", out var oid))
                        {
                            rejectOptId = oid;
                            break;
                        }
                    }
                }
            }

            if (rejectOptId.HasValue)
            {
                SendMessage(new
                {
                    jsonrpc = "2.0",
                    id,
                    result = new { outcome = new { outcome = "selected", optionId = rejectOptId.Value.GetString() } }
                });
            }
            else
            {
                SendMessage(new
                {
                    jsonrpc = "2.0",
                    id,
                    result = new { outcome = new { outcome = "cancelled" } }
                });
            }
        }
        else
        {
            SendMessage(new
            {
                jsonrpc = "2.0",
                id,
                error = new { code = -32601, message = "Method not supported" }
            });
        }
    }

    private void HandleNotification(string method, JsonElement msg)
    {
        if (method != "session/update") return;
        if (!msg.TryGetProperty("params", out var p) ||
            !p.TryGetProperty("update", out var update))
            return;

        var sessionUpdate = update.TryGetProperty("sessionUpdate", out var su) ? su.GetString() : null;

        if (sessionUpdate == "agent_message_chunk")
        {
            if (update.TryGetProperty("content", out var content) &&
                content.TryGetProperty("type", out var ct) && ct.GetString() == "text" &&
                content.TryGetProperty("text", out var text))
            {
                var t = text.GetString() ?? "";
                _promptText += t;
                OnChunk?.Invoke(t);
            }
        }
        else if (sessionUpdate == "agent_thought_chunk")
        {
            if (update.TryGetProperty("content", out var content) &&
                content.TryGetProperty("type", out var ct) && ct.GetString() == "text" &&
                content.TryGetProperty("text", out var text))
            {
                OnThinkingChunk?.Invoke(text.GetString() ?? "");
            }
        }
    }

    private void OnErrorData(object sender, DataReceivedEventArgs e)
    {
        // stderr — ignore for now
    }

    private void OnExited(object? sender, EventArgs e)
    {
        _proc = null;
        _sessionId = null;

        // Fail all pending requests
        foreach (var kvp in _pending)
        {
            if (_pending.TryRemove(kvp.Key, out var tcs))
                tcs.TrySetException(new InvalidOperationException("ACP process exited"));
        }
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}

public class CopilotModelInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
