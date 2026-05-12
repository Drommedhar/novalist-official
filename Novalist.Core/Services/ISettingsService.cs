using Novalist.Core.Models;

namespace Novalist.Core.Services;

public interface ISettingsService
{
    AppSettings Settings { get; }
    Task LoadAsync();
    Task SaveAsync();
    void AddRecentProject(string name, string path, string coverImagePath = "");
    void RemoveRecentProject(string path);
}
