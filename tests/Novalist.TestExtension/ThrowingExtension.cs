using System.Threading;
using System.Threading.Tasks;
using Novalist.Sdk;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Services;

namespace Novalist.TestExtension;

/// <summary>
/// Test-only extension that throws from Initialize and/or Shutdown when the
/// corresponding environment variable is set, and provides an
/// env-var-controlled grammar contributor. Used to exercise the host's
/// error-handling paths for misbehaving extensions. Not a usable extension.
/// </summary>
public sealed class ThrowingExtension : IExtension, IGrammarCheckContributor
{
    public string Id => "test.throwing";
    public string DisplayName => "Throwing Test Extension";
    public string Description => "Throws on demand for host error-path tests.";
    public string Version => "1.0.0";
    public string Author => "Tests";

    public void Initialize(IHostServices host)
    {
        if (Environment.GetEnvironmentVariable("NOVALIST_TEST_THROW_INIT") == "1")
            throw new InvalidOperationException("init boom");
    }

    public void Shutdown()
    {
        if (Environment.GetEnvironmentVariable("NOVALIST_TEST_THROW_SHUTDOWN") == "1")
            throw new InvalidOperationException("shutdown boom");
    }

    // ── IGrammarCheckContributor (env-controlled for host tests) ────
    // NOVALIST_TEST_GRAMMAR = "throw"  -> CheckAsync throws (swallowed by host)
    //                       = "cancel" -> CheckAsync throws OperationCanceledException
    // The contributor is only enabled while one of those modes is set.

    public string GrammarCheckName => "Throwing Grammar";

    public bool IsGrammarCheckEnabled
        => Environment.GetEnvironmentVariable("NOVALIST_TEST_GRAMMAR") is "throw" or "cancel";

    public Task<GrammarCheckResult> CheckAsync(string plainText, string language, CancellationToken cancellationToken = default)
    {
        var mode = Environment.GetEnvironmentVariable("NOVALIST_TEST_GRAMMAR");
        if (mode == "cancel")
            throw new OperationCanceledException();
        if (mode == "throw")
            throw new InvalidOperationException("grammar boom");
        return Task.FromResult(new GrammarCheckResult());
    }
}
