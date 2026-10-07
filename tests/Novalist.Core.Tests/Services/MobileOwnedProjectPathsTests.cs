using System.Text.Json;
using Novalist.Mobile.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

public sealed class MobileOwnedProjectPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nl-owned-paths-" + Guid.NewGuid().ToString("N"));
    private string OldDocuments => Path.Combine(_root, "old-container", "Documents");
    private string Documents => Path.Combine(_root, "new-container", "Documents");
    private string Registry => Path.Combine(_root, "owned-project-paths.json");

    public MobileOwnedProjectPathsTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task RecordedLocalProjectMovesOnlyToItsOwnIdentity()
    {
        var original = CreateProject(OldDocuments, "Book", "project-a");
        await new OwnedProjectPaths(OldDocuments, Registry).RecordAsync(original);
        var replacement = CreateProject(Documents, "Book", "project-a");
        Assert.Equal(replacement, new OwnedProjectPaths(Documents, Registry).Resolve(original));
        CreateProject(Documents, "Book", "project-b");
        Assert.Null(new OwnedProjectPaths(Documents, Registry).Resolve(original));
    }

    [Fact]
    public async Task UnrecordedAndExternalDocumentsPathsNeverAliasLocalProjects()
    {
        var external = CreateProject(Path.Combine(_root, "external", "Documents"), "Book", "project-a");
        var resolver = new OwnedProjectPaths(Documents, Registry);
        CreateProject(Documents, "Book", "project-a");
        await resolver.RecordAsync(external);
        Assert.Null(resolver.Resolve(external));
        Assert.Null(resolver.Resolve(Path.Combine(OldDocuments, "Book")));
        Assert.False(File.Exists(Registry));
    }

    [Fact]
    public async Task MatchingSuffixOutsideDocumentsCannotBeRecorded()
    {
        var outside = CreateProject(OldDocuments + "-other", "Book", "project-a");
        await new OwnedProjectPaths(OldDocuments, Registry).RecordAsync(outside);
        Assert.False(File.Exists(Registry));
    }

    [Fact]
    public async Task SuccessiveContainerMovesRetainTheRepairedPathWithoutReopening()
    {
        var original = CreateProject(OldDocuments, "Book", "project-a");
        await new OwnedProjectPaths(OldDocuments, Registry).RecordAsync(original);
        var intermediate = CreateProject(Documents, "Book", "project-a");
        Assert.Equal(intermediate, new OwnedProjectPaths(Documents, Registry).Resolve(original));
        var nextDocuments = Path.Combine(_root, "third-container", "Documents");
        var latest = CreateProject(nextDocuments, "Book", "project-a");

        // Recents publishes the resolved path without opening the project or recording it again.
        var resolver = new OwnedProjectPaths(nextDocuments, Registry);
        Assert.Equal(latest, resolver.Resolve(intermediate));
        Assert.Equal(latest, resolver.Resolve(original));
    }

    [Fact]
    public async Task RelocationAliasesDoNotAuthorizeExternalMatchingSuffixesOrChangedIdentities()
    {
        var original = CreateProject(OldDocuments, "Book", "project-a");
        await new OwnedProjectPaths(OldDocuments, Registry).RecordAsync(original);
        var intermediate = CreateProject(Documents, "Book", "project-a");
        Assert.Equal(intermediate, new OwnedProjectPaths(Documents, Registry).Resolve(original));
        var external = CreateProject(Path.Combine(_root, "external", "Documents"), "Book", "project-a");
        var nextDocuments = Path.Combine(_root, "third-container", "Documents");
        CreateProject(nextDocuments, "Book", "project-a");
        var resolver = new OwnedProjectPaths(nextDocuments, Registry);
        Assert.Null(resolver.Resolve(external));
        CreateProject(nextDocuments, "Book", "different-project");
        Assert.Null(resolver.Resolve(intermediate));
    }

    [Fact]
    public async Task ConcurrentRecordsFromSeparateInstancesRetainEveryProject()
    {
        var originals = Enumerable.Range(0, 12)
            .Select(index => CreateProject(OldDocuments, "Book-" + index, "project-" + index)).ToArray();
        await Task.WhenAll(originals.Select(path => new OwnedProjectPaths(OldDocuments, Registry).RecordAsync(path)));
        using var registry = JsonDocument.Parse(await File.ReadAllTextAsync(Registry));
        var keys = registry.RootElement.EnumerateObject().Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(originals.Length, keys.Count);
        Assert.All(originals, path => Assert.Contains(path, keys));
    }

    [Fact]
    public async Task CorruptAndMissingCandidateManifestsDoNotRelocate()
    {
        var original = CreateProject(OldDocuments, "Book", "project-a");
        await new OwnedProjectPaths(OldDocuments, Registry).RecordAsync(original);
        var resolver = new OwnedProjectPaths(Documents, Registry);
        Assert.Null(resolver.Resolve(original));
        var candidate = CreateProject(Documents, "Book", "project-a");
        File.WriteAllText(Path.Combine(candidate, ".novalist", "project.json"), "broken");
        Assert.Null(resolver.Resolve(original));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("bad\0path")]
    public void MalformedRegistryCannotEscapeOrCrashResolution(string relative)
    {
        File.WriteAllText(Registry, JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["old-project"] = new { RelativePath = relative, ProjectId = "project-a" }
        }));
        Assert.Null(new OwnedProjectPaths(Documents, Registry).Resolve("old-project"));
    }

    private static string CreateProject(string documents, string name, string id)
    {
        var project = Path.Combine(documents, name);
        Directory.CreateDirectory(Path.Combine(project, ".novalist"));
        File.WriteAllText(Path.Combine(project, ".novalist", "project.json"), JsonSerializer.Serialize(new { id }));
        return project;
    }

    public void Dispose() => Directory.Delete(_root, true);
}
