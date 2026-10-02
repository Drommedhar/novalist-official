using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Core.Models;
using System.Text.Json.Nodes;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class FolderImportRpcTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task FolderImport_PagesAndFiltersFolderRulesThenImportsAndReleasesTheSession()
    {
        using var workspace = new Workspace(Path.Combine(_dir.Path, "settings"));
        await workspace.Projects.CreateProjectAsync(_dir.Path, "Novel", "Book");
        var root = Path.Combine(_dir.Path, "source");
        for (var index = 0; index < 75; index++)
        {
            var folder = Path.Combine(root, $"Folder{index:D2}");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "one.md"), "# One\n\nContent");
        }
        File.WriteAllText(Path.Combine(root, "ignored.pdf"), "ignored");
        var rpc = new FolderImportRpc(workspace);
        Assert.Throws<InvalidOperationException>(() => rpc.Folders("missing"));
        var scan = await rpc.ScanAsync(root);
        Assert.Equal(75, scan.Total);
        Assert.Equal(75, scan.Folders);
        Assert.Equal(1, scan.Unsupported);
        var first = rpc.Folders(scan.SessionId);
        Assert.Equal(75, first.Total);
        Assert.Equal(30, first.Items.Length);
        Assert.Equal("Folder00", first.Items[0].Path);
        Assert.Equal("Folder30", rpc.Folders(scan.SessionId, "", 30).Items[0].Path);
        Assert.Equal("Folder07", Assert.Single(rpc.Folders(scan.SessionId, " folder07 ").Items).Path);
        Assert.Equal(75, rpc.Folders(scan.SessionId, "", -9, 999).Items.Length);
        Assert.Single(rpc.Folders(scan.SessionId, "", 0, 0).Items);
        Assert.Empty(rpc.Folders(scan.SessionId, "absent").Items);
        Assert.Empty(rpc.Folders(scan.SessionId, "", 100).Items);
        Assert.False(rpc.Start(scan.SessionId, "research", new() { ["Folder00"] = "skip" }, "Content").Done);
        Assert.Equal(40, (await rpc.BatchAsync(scan.SessionId)).Processed);
        var result = await rpc.BatchAsync(scan.SessionId);
        Assert.True(result.Done);
        Assert.Equal(74, result.Imported);
        Assert.Equal(1, result.Skipped);
        rpc.Release("other");
        Assert.Equal(75, rpc.Folders(scan.SessionId).Total);
        rpc.Release(scan.SessionId);
        Assert.Throws<InvalidOperationException>(() => rpc.Folders(scan.SessionId));
        var next = await rpc.ScanAsync(root);
        Assert.NotEqual(scan.SessionId, next.SessionId);
        Assert.Throws<InvalidOperationException>(() => rpc.Start(scan.SessionId, "research", [], "Content"));
        rpc.Start(next.SessionId, "research", [], "Content", "tag");
        await rpc.BatchAsync(next.SessionId);
        Assert.Equal(1, (await rpc.BatchAsync(next.SessionId)).Imported);
    }

    [Fact]
    public async Task ExportSchema_UsesTheOpenBooksDefinitionsWithoutIncludingStoryData()
    {
        using var workspace = new Workspace(Path.Combine(_dir.Path, "settings"));
        var rpc = new FolderImportRpc(workspace);
        var output = Path.Combine(_dir.Path, "novalist-import.schema.json");
        await Assert.ThrowsAsync<InvalidOperationException>(() => rpc.ExportSchemaAsync(output));
        await workspace.Projects.CreateProjectAsync(_dir.Path, "Novel", "Book");
        workspace.Projects.ActiveBook!.CharacterTemplates = [new() { Id = "people", Name = "People", CustomPropertyDefs = [new() { Key = "Skill", Prompt = "What can they do?" }] }];
        workspace.Projects.ActiveBook.ActiveCharacterTemplateId = "people";
        workspace.Projects.CurrentProject!.CustomEntityTypes.Add(new() { TypeKey = "faction", FolderName = "Factions", DefaultFields = [new() { Key = "members", Type = CustomPropertyType.Int }] });
        workspace.Projects.CurrentProject.ResearchItems.Add(new() { Title = "Private story sentinel", Content = "Private writing sentinel" });
        await rpc.ExportSchemaAsync(output);
        var text = File.ReadAllText(output);
        Assert.DoesNotContain("Private story sentinel", text);
        Assert.DoesNotContain("Private writing sentinel", text);
        var branches = (JsonArray)JsonNode.Parse(text)!["oneOf"]!;
        Assert.Equal(7, branches.Count);
        var character = branches.Single(branch => branch!["properties"]!["target"]!["const"]!.GetValue<string>() == "character")!;
        Assert.Equal("people", character["properties"]!["data"]!["properties"]!["templateId"]!["const"]!.GetValue<string>());
        Assert.Contains("What can they do?", character["properties"]!["data"]!["properties"]!["customProperties"]!["properties"]!["Skill"]!["description"]!.GetValue<string>());
        await Assert.ThrowsAsync<IOException>(() => rpc.ExportSchemaAsync(Path.Combine(output, "schema.json")));
    }
}
