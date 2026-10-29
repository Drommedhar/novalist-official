namespace Novalist.Core.Services;

/// <summary>Refreshes cached path-to-document mappings before an optimistic save check.</summary>
public interface IFileMetadataCache
{
    Task RefreshMetadataAsync(string path);
}
