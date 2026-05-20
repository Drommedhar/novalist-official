using Novalist.Core.Services;

namespace Novalist.Core.Tests.TestHelpers;

/// <summary>
/// Scriptable <see cref="IProcessRunner"/>. The responder maps the argument
/// array to a canned (exit, stdout, stderr) tuple, so process-based services
/// can be exercised with no real binary.
/// </summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Func<string[], (int, string, string)> _responder;
    public readonly List<string[]> Calls = new();

    /// <summary>When set, RunAsync throws this instead of returning.</summary>
    public Exception? ThrowException { get; set; }

    /// <summary>Convenience for the common "launch failed" case.</summary>
    public bool Throw
    {
        get => ThrowException != null;
        set => ThrowException = value ? new InvalidOperationException("process launch failed") : null;
    }

    public FakeProcessRunner(Func<string[], (int, string, string)> responder)
        => _responder = responder;

    public Task<(int ExitCode, string Output, string Error)> RunAsync(
        string fileName, string? workingDirectory, params string[] args)
        => RunAsync(fileName, workingDirectory, CancellationToken.None, args);

    public Task<(int ExitCode, string Output, string Error)> RunAsync(
        string fileName, string? workingDirectory, CancellationToken cancellationToken, params string[] args)
    {
        Calls.Add(args);
        if (ThrowException != null) throw ThrowException;
        var (e, o, err) = _responder(args);
        return Task.FromResult((e, o, err));
    }
}
