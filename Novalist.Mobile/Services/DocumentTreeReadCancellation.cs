namespace Novalist.Mobile.Services;

internal static class DocumentTreeReadCancellation
{
    private static readonly AsyncLocal<CancellationToken> Token = new();
    public static CancellationToken Current => Token.Value;

    public static IDisposable Enter(CancellationToken cancellationToken)
    {
        var scope = new Scope(Token.Value);
        Token.Value = cancellationToken;
        return scope;
    }

    private sealed class Scope(CancellationToken previous) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            Token.Value = previous;
            _disposed = true;
        }
    }
}
