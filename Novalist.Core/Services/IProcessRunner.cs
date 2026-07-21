using System.Diagnostics;

namespace Novalist.Core.Services;

/// <summary>
/// Abstraction over launching an external process and capturing its result.
/// Lets services that shell out (git, OS tools) be unit-tested with a fake
/// runner instead of invoking real binaries.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs <paramref name="fileName"/> with <paramref name="args"/> in the
    /// optional working directory and returns the exit code plus captured
    /// stdout/stderr.
    /// </summary>
    Task<(int ExitCode, string Output, string Error)> RunAsync(
        string fileName, string? workingDirectory, params string[] args);

    /// <summary>
    /// Cancellable variant. On cancellation the process is killed and an
    /// <see cref="OperationCanceledException"/> is thrown.
    /// </summary>
    Task<(int ExitCode, string Output, string Error)> RunAsync(
        string fileName, string? workingDirectory, CancellationToken cancellationToken, params string[] args);
}

/// <summary>Production <see cref="IProcessRunner"/> backed by <see cref="Process"/>.</summary>
public sealed class ProcessRunner : IProcessRunner
{
    public Task<(int ExitCode, string Output, string Error)> RunAsync(
        string fileName, string? workingDirectory, params string[] args)
        => RunAsync(fileName, workingDirectory, CancellationToken.None, args);

    public async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string fileName, string? workingDirectory, CancellationToken cancellationToken, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        if (!string.IsNullOrEmpty(workingDirectory))
            psi.WorkingDirectory = workingDirectory;

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        // Process.Start(psi) only returns null for UseShellExecute reuse scenarios,
        // which cannot happen here (UseShellExecute = false). A genuine launch
        // failure throws, which callers catch.
        using var process = Process.Start(psi)!;

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
            throw;
        }

        var output = await outputTask;
        var error = await errorTask;

        return (process.ExitCode, output, error);
    }
}

/// <summary>
/// <see cref="IProcessRunner"/> for sandboxed platforms (iOS/Android) where
/// launching external processes is forbidden. Every run reports a non-zero exit
/// with no output, so process-based capabilities degrade to "unavailable"
/// instead of throwing: <see cref="GitService"/> sees <c>git --version</c> fail
/// and reports Git not installed, and repository probes find no repo. Injected in
/// place of <see cref="ProcessRunner"/> by the mobile host.
/// </summary>
public sealed class UnavailableProcessRunner : IProcessRunner
{
    // 127 is the shell convention for "command not found".
    private const int ExitNotFound = 127;
    private const string Message = "Process execution is not available on this platform.";

    private static Task<(int ExitCode, string Output, string Error)> Unavailable()
        => Task.FromResult((ExitNotFound, string.Empty, Message));

    public Task<(int ExitCode, string Output, string Error)> RunAsync(
        string fileName, string? workingDirectory, params string[] args)
        => Unavailable();

    public Task<(int ExitCode, string Output, string Error)> RunAsync(
        string fileName, string? workingDirectory, CancellationToken cancellationToken, params string[] args)
        => Unavailable();
}
