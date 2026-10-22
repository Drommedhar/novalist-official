#if NOVALIST_COLLABORATION
using System.Text.Json;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed partial class Workspace
{
    private async Task<string> NormalizeSharedParentAsync(SharedFieldBinding binding, string value)
    {
        if (binding.Custom || binding.Metadata is not null || binding.Key != "parent") return value;
        var root = sharedRoot ?? throw new InvalidOperationException("The shared project is no longer open.");
        var project = Projects.CurrentProject ?? throw new InvalidOperationException("The shared project is no longer open.");
        var book = project.Books.SingleOrDefault(book => binding.Path.StartsWith(book.FolderName + '/', StringComparison.OrdinalIgnoreCase)) ?? Projects.ActiveBook;
        var places = new List<LocationData>();
        foreach (var candidate in ActiveFieldBindings.Where(field => !field.Custom && field.Metadata is null && field.Key == "parent"))
        {
            if (!candidate.Path.StartsWith(book?.FolderName + '/', StringComparison.OrdinalIgnoreCase) &&
                !candidate.Path.StartsWith(project.WorldBibleFolder + '/', StringComparison.OrdinalIgnoreCase)) continue;
            var path = SharedProjectPath.Resolve(root, candidate.Path);
            if (!await FileService.ExistsAsync(path)) continue;
            places.Add(JsonSerializer.Deserialize<LocationData>(await FileService.ReadTextAsync(path))
                ?? throw new InvalidDataException("Invalid shared location."));
        }
        var child = places.Single(place => place.Id == Path.GetFileNameWithoutExtension(binding.Path));
        if (!PlaceHierarchy.CanReparent(places, child, value))
            throw new InvalidOperationException("That parent would create an invalid location hierarchy.");
        return value.Trim();
    }
}
#endif
