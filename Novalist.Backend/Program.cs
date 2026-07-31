namespace Novalist.Backend;

// Entry point: binds the JSON-RPC host to the real process stdio streams.
// All bootstrap — no unit-testable logic; BackendHost carries the testable parts.
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
internal class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Anything on the command line is a tool run rather than a server run.
        // No arguments is the path the app takes and is untouched.
        var request = CommandLine.Parse(args);
        if (request.Help)
        {
            Console.Error.WriteLine(CommandLine.Usage);
            return 0;
        }
        if (request.Error != null)
        {
            Console.Error.WriteLine(request.Error);
            Console.Error.WriteLine(CommandLine.Usage);
            return 64;   // EX_USAGE
        }
        if (!request.Serve) return await CommandLine.ExportAsync(request, Console.Error);

        // stdout is reserved for JSON-RPC frames. Capture the raw stream first,
        // then reroute Console.Out so any stray write from a Core service lands
        // on stderr instead of corrupting the frame stream.
        var stdout = Console.OpenStandardOutput();
        var stdin = Console.OpenStandardInput();
        BackendHost.GuardStandardOutput();

        var settingsDir = Environment.GetEnvironmentVariable("NOVALIST_SETTINGS_DIR");
        // First run after unifying with Electron's userData: carry over a legacy
        // ~/.config/Novalist install so settings + extensions are not orphaned.
        // Only when the app chose userData itself (flag set by the main process);
        // never when a test/tool supplied its own NOVALIST_SETTINGS_DIR.
        if (Environment.GetEnvironmentVariable("NOVALIST_ALLOW_LEGACY_MIGRATION") == "1")
            DataMigration.MigrateLegacyIfNeeded(settingsDir);
        using var host = new BackendHost(settingsDir);
        await host.RunAsync(stdout, stdin);
        return 0;
    }
}
