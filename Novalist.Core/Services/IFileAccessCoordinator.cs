namespace Novalist.Core.Services;

/// <summary>
/// Platform coordination with cloud file providers. Accessors are synchronous:
/// all IO must finish before the platform releases its coordinated access.
/// The supplied paths are authoritative; a provider can relocate an item.
/// </summary>
public interface IFileAccessCoordinator
{
    Task<T> ReadAsync<T>(string path, Func<string, T> access);
    Task<T> WriteAsync<T>(string path, bool deleting, Func<string, T> access);
    Task<T> MoveAsync<T>(string source, string destination, Func<string, string, T> access);
}
