using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Novalist.Backend.Dictation;

public interface IAppleSpeechBridge
{
    Task<string> RequestAsync(object request, CancellationToken token);
}

/// <summary>Small C ABI around Apple's Swift-only SpeechAnalyzer API.</summary>
// Swift callbacks and AOT P/Invoke require the Apple build/device checks.
[ExcludeFromCodeCoverage]
public sealed class AppleSpeechBridge : IAppleSpeechBridge
{
    private static long _nextId;
    private static readonly ConcurrentDictionary<long, TaskCompletionSource<string>> Pending = new();

    public async Task<string> RequestAsync(object request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Pending[id] = completion;
        try
        {
            Begin(id, JsonSerializer.Serialize(request));
            using var cancel = token.Register(() =>
            {
                Cancel(id);
                completion.TrySetCanceled(token);
            });
            return await completion.Task;
        }
        finally { Pending.TryRemove(id, out _); }
    }

    // Native entry points are exercised by the Apple build/device checks.
    private static unsafe void Begin(long id, string request)
    {
        if (OperatingSystem.IsIOS()) IosRequest(id, request, &Reply);
        else MacRequest(id, request, &Reply);
    }

    private static void Cancel(long id)
    {
        if (OperatingSystem.IsIOS()) IosCancel(id);
        else MacCancel(id);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Reply(long id, IntPtr json)
    {
        if (!Pending.TryGetValue(id, out var completion)) return;
        try
        {
            using var reply = JsonDocument.Parse(Marshal.PtrToStringUTF8(json) ?? "{}");
            if (reply.RootElement.TryGetProperty("error", out _))
                completion.TrySetException(new InvalidOperationException("System speech could not complete the request."));
            else completion.TrySetResult(reply.RootElement.GetProperty("result").GetRawText());
        }
        catch { completion.TrySetException(new InvalidOperationException("Invalid system speech response.")); }
    }

    [DllImport("__Internal", EntryPoint = "nl_speech_request", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe void IosRequest(long id, [MarshalAs(UnmanagedType.LPUTF8Str)] string request,
        delegate* unmanaged[Cdecl]<long, IntPtr, void> callback);
    [DllImport("__Internal", EntryPoint = "nl_speech_cancel", CallingConvention = CallingConvention.Cdecl)]
    private static extern void IosCancel(long id);
    [DllImport("NovalistSpeech", EntryPoint = "nl_speech_request", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe void MacRequest(long id, [MarshalAs(UnmanagedType.LPUTF8Str)] string request,
        delegate* unmanaged[Cdecl]<long, IntPtr, void> callback);
    [DllImport("NovalistSpeech", EntryPoint = "nl_speech_cancel", CallingConvention = CallingConvention.Cdecl)]
    private static extern void MacCancel(long id);
}
