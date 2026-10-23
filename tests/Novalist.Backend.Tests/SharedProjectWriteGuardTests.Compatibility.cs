using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed partial class SharedProjectWriteGuardTests
{
    [Fact]
    public async Task FormattingAnExistingCopyDoesNotPreventSharedMetadataAndProseEditing()
    {
        await using var fixture = await SharedProjectFixture.StartAsync(attach: false);
        var ct = TestContext.Current.CancellationToken;
        var field = fixture.State.Fields.Single(binding => binding.Path.EndsWith(fixture.CharacterId + ".json", StringComparison.Ordinal) && binding.Key == "age");
        var path = Path.Combine(fixture.Root, field.Path);
        var before = await File.ReadAllBytesAsync(path, ct);
        var entity = Assert.IsType<JsonObject>(JsonNode.Parse(before));
        await File.WriteAllTextAsync(path, "\n  " + entity.ToJsonString() + "\n", ct);
        Assert.NotEqual(before, await File.ReadAllBytesAsync(path, ct));

        await fixture.AttachAsync(fixture.State);
        var focus = Guid.NewGuid();
        await fixture.Desktop.InvokeAsync("collaboration/acquireField", fixture.SessionId, field.ItemId, focus);
        await fixture.Desktop.InvokeAsync("collaboration/writeField", fixture.SessionId, field.ItemId, focus, "43");
        await fixture.Desktop.InvokeAsync("collaboration/releaseField", fixture.SessionId, field.ItemId, focus);
        var prose = fixture.State.Documents.Single(document => document.Path == field.Path && document.TextField?.Key == "distinguishingFeatures");
        await fixture.Desktop.InvokeAsync("collaboration/writeScene", fixture.SessionId, prose.ItemId, Encoding.UTF8.GetBytes("text:A silver streak."));

        var updated = await fixture.Desktop.InvokeAsync<JsonElement>("entities/get", "character", fixture.CharacterId);
        Assert.Equal("43", updated.GetProperty("age").GetString());
        Assert.Equal("A silver streak.", updated.GetProperty("distinguishingFeatures").GetString());
        Assert.True(await fixture.Desktop.InvokeAsync<bool>("system/barrier"));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Recovered edits")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StructuralUpdatesKeepSeparatelySharedArcProseOutOfTheFileState(bool omitArc)
    {
        await using var fixture = await SharedProjectFixture.StartAsync(attach: false);
        var ct = TestContext.Current.CancellationToken;
        var relative = fixture.State.Fields.First(binding => binding.Path.EndsWith(fixture.CharacterId + ".json", StringComparison.Ordinal)).Path;
        var path = Path.Combine(fixture.Root, relative);
        var entity = Assert.IsType<JsonObject>(JsonNode.Parse(await File.ReadAllBytesAsync(path, ct)));
        if (omitArc) entity.Remove("arc");
        else entity["arc"] = null;
        await File.WriteAllTextAsync(path, entity.ToJsonString(), ct);
        await fixture.CaptureAsync();
        await fixture.AttachAsync(fixture.State);
        var entry = await fixture.Desktop.InvokeAsync<JsonElement>("entities/get", "character", fixture.CharacterId);
        var prose = entry.GetProperty("sharedProse").GetProperty("fields").GetProperty("arc:start").GetGuid();
        await fixture.Desktop.InvokeAsync("collaboration/writeScene", fixture.SessionId, prose, Encoding.UTF8.GetBytes("text:Afraid to trust."));

        await fixture.Desktop.InvokeAsync("entities/setStateOverrides", "character", fixture.CharacterId,
            new[] { new StateOverrideDto(null, "Opening", null, "Captain", "Description", null, "Note", false) });

        Assert.Equal("Afraid to trust.", (await fixture.Desktop.InvokeAsync<ArcDto>("arcs/get", fixture.CharacterId)).Start);
        var files = await fixture.ReadCommittedFilesAsync();
        var published = Assert.IsType<JsonObject>(JsonNode.Parse(files[relative]));
        Assert.Null(published["arc"]);
        Assert.Equal(!omitArc, published.ContainsKey("arc"));
        Assert.Equal("Captain", Assert.Single(Assert.IsType<JsonArray>(published["stateOverrides"]))?["name"]?.GetValue<string>());
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Recovered edits")));
    }
}
