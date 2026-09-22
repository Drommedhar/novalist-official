using Novalist.Core.Services;

namespace Novalist.Core.Tests.TestHelpers;

/// <summary>Models a provider that grants access, downloads, redirects, or fails.</summary>
public sealed class RecordingFileCoordinator : IFileAccessCoordinator
{
    public List<(string Operation, string Path)> Accesses { get; } = [];
    public Action<string>? BeforeRead { get; set; }
    public Action<string, bool>? BeforeWrite { get; set; }
    public Func<string, string> Resolve { get; set; } = path => path;

    public Task<T> ReadAsync<T>(string path, Func<string, T> access)
    {
        Accesses.Add(("read", path));
        BeforeRead?.Invoke(path);
        return Task.FromResult(access(Resolve(path)));
    }

    public Task<T> WriteAsync<T>(string path, bool deleting, Func<string, T> access)
    {
        Accesses.Add((deleting ? "delete" : "write", path));
        BeforeWrite?.Invoke(path, deleting);
        return Task.FromResult(access(Resolve(path)));
    }

    public Task<T> MoveAsync<T>(string source, string destination, Func<string, string, T> access)
    {
        Accesses.Add(("move", source));
        Accesses.Add(("destination", destination));
        return Task.FromResult(access(Resolve(source), Resolve(destination)));
    }
}
