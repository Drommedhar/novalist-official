using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Serializes every test class that reads or writes backend process-wide state.
/// xUnit runs distinct test classes in parallel, so without this grouping two
/// classes can stomp on the same static between one test's write and its assert.
/// </summary>
/// <remarks>
/// The shared statics are:
/// <list type="bullet">
///   <item><c>Loc.Instance.CurrentLanguage</c> — the extension-facing UI language.</item>
///   <item><c>ExtensionsRpc.WebviewPosted</c> — set by every <c>BackendHost.Attach</c>.</item>
///   <item><c>HostNotifications.Error</c> — likewise.</item>
///   <item><c>Log</c> — its opt-in flag and test sink are process-wide.</item>
/// </list>
/// Add a class here whenever it touches one of them; the alternative is a test
/// that passes alone and fails one run in five.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class BackendStaticsCollection
{
    public const string Name = "BackendStatics";
}
