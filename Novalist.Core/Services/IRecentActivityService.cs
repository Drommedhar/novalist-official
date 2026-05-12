using Novalist.Core.Models;

namespace Novalist.Core.Services;

public interface IRecentActivityService
{
    IReadOnlyList<ActivityItem> Recent { get; }
    Task LoadAsync(string projectRoot);
    Task LogAsync(ActivityItem item);
    event Action? Changed;
}
