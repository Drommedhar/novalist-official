using System.Text.Json;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Sync.Client.Tests;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class SharedMediaMetadataTests
{
    [Fact]
    public async Task MaterializingInheritedImagesBindsTheirScopedNamesBeforeTheNextRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var desktop = new HostedDesktop(Path.Combine(directory.Path, "settings"), identity);
        await desktop.Rpc.InvokeAsync("project/create", directory.Path, "Shared", "Book");
        var chapter = Assert.Single((await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/createChapter", "Opening")).Chapters);
        await desktop.Rpc.InvokeAsync("project/createScene", chapter.Guid, "Arrival");
        var character = await desktop.Rpc.InvokeAsync<JsonElement>("entities/create", "character", "Mira");
        var id = character.GetProperty("id").GetString();
        var imagePath = Path.Combine(directory.Path, "portrait.png");
        await File.WriteAllBytesAsync(imagePath, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="), ct);
        character = await desktop.Rpc.InvokeAsync<JsonElement>("entities/addImage", "character", id, imagePath, true);
        var image = character.GetProperty("images")[0];
        await desktop.Rpc.InvokeAsync("entities/setOverride", id, chapter.Guid, "Arrival", new { age = "27" });
        await desktop.SignInAsync(server.Address);
        await desktop.Rpc.InvokeAsync("collaboration/create", server.Address);

        await desktop.Rpc.InvokeAsync("entities/setOverrideImages", id, chapter.Guid, "Arrival",
            new[] { new EntityImageDto(image.GetProperty("name").GetString()!, image.GetProperty("path").GetString()!) });

        character = await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "character", id);
        var metadata = character.GetProperty("sharedFields").GetProperty("metadata");
        Assert.True(metadata.TryGetProperty($"override:{chapter.Guid}:Arrival:image:{image.GetProperty("path").GetString()}:name", out var binding),
            string.Join(", ", metadata.EnumerateObject().Where(property => property.Name.Contains("image:", StringComparison.Ordinal)).Select(property => property.Name)));
        Assert.NotEqual(Guid.Empty, binding.GetGuid());
    }
}
