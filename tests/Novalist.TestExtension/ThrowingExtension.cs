using Novalist.Sdk;
using Novalist.Sdk.Services;

namespace Novalist.TestExtension;

/// <summary>
/// Test-only extension that throws from Initialize and/or Shutdown when the
/// corresponding environment variable is set. Used to exercise the host's
/// error-handling paths for misbehaving extensions. Not a usable extension.
/// </summary>
public sealed class ThrowingExtension : IExtension
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
}
