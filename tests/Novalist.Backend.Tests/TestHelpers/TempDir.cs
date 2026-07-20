namespace Novalist.Backend.Tests.TestHelpers;

/// <summary>Disposable temp directory for filesystem-bound service tests.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "novalist-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Combine(params string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());

    public void Dispose() => ForceDelete(Path);

    /// <summary>
    /// Recursively deletes a directory, best-effort. git marks its object/pack
    /// files read-only; on Windows <see cref="Directory.Delete(string, bool)"/>
    /// then throws UnauthorizedAccessException, so clear the read-only attribute
    /// on every file first. Any residual failure is swallowed (temp cleanup).
    /// </summary>
    public static void ForceDelete(string path)
    {
        if (!Directory.Exists(path)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); }
                catch { /* ignore a single unreadable entry */ }
            }
        }
        catch { /* enumeration race; fall through to delete */ }

        try { Directory.Delete(path, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
