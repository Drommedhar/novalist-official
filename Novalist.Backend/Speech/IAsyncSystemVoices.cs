namespace Novalist.Backend.Speech;

public interface IAsyncSystemVoices
{
    Task<IReadOnlyList<SystemVoice>> ListAsync();
    Task<bool> SpeakAsync(string text, string? voiceId, double rate);
    void Stop();
}
