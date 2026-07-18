namespace Novalist.Backend;

// Entry point: binds the JSON-RPC host to the real process stdio streams.
// All bootstrap — no unit-testable logic; BackendHost carries the testable parts.
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
internal class Program
{
    public static async Task<int> Main(string[] args)
    {
        // stdout is reserved for JSON-RPC frames. Capture the raw stream first,
        // then reroute Console.Out so any stray write from a Core service lands
        // on stderr instead of corrupting the frame stream.
        var stdout = Console.OpenStandardOutput();
        var stdin = Console.OpenStandardInput();
        BackendHost.GuardStandardOutput();

        using var host = new BackendHost();
        await host.RunAsync(stdout, stdin);
        return 0;
    }
}
