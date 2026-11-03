using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public sealed class EntityLegacyIdentityTests
{
    [Fact]
    public async Task LoadingLegacySectionsKeepsFilesUnchangedAndSavingRetainsTheirIdentitiesAfterReordering()
    {
        using var directory = new TempDir();
        var projects = new ProjectService(new FileService());
        await projects.CreateProjectAsync(directory.Path, "Novel", "Book");
        var root = projects.ActiveBookRoot!;
        var path = Path.Combine(root, "Characters", "writer.json");
        const string original = """
            {"id":"writer","name":"Writer","sections":[{"title":"First","content":"First prose."},{"id":"legacy-section-0","title":"Existing","content":"Second prose."}],"stateOverrides":[{"chapter":"Opening","description":"Beginning."}],"chapterOverrides":[{"chapter":"Opening","sections":[{"title":"Later","content":"Later prose."}]}]}
            """;
        await File.WriteAllTextAsync(path, original, TestContext.Current.CancellationToken);
        var entities = new EntityService(projects);

        var initial = Assert.Single(await entities.LoadCharactersAsync());
        var again = Assert.Single(await entities.LoadCharactersAsync());

        Assert.Equal(original, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(initial.Sections.Select(section => section.Id), again.Sections.Select(section => section.Id));
        Assert.Equal(2, initial.Sections.Select(section => section.Id).Distinct().Count());
        Assert.Equal(initial.StateOverrides[0].Id, again.StateOverrides[0].Id);
        Assert.Equal(initial.ChapterOverrides[0].Sections![0].Id, again.ChapterOverrides[0].Sections![0].Id);
        var firstId = initial.Sections[0].Id;
        initial.Sections.Reverse();
        initial.Sections.Insert(0, new EntitySection { Title = "New", Content = "Inserted." });
        await entities.SaveCharacterAsync(initial);
        var saved = Assert.Single(await entities.LoadCharactersAsync());
        Assert.Equal(firstId, saved.Sections[2].Id);
        Assert.Equal("First prose.", saved.Sections[2].Content);
    }

    [Fact]
    public async Task DuplicateSectionIdentitiesAreRejectedWithoutOverwritingTheEntry()
    {
        using var directory = new TempDir();
        var projects = new ProjectService(new FileService());
        await projects.CreateProjectAsync(directory.Path, "Novel", "Book");
        var path = Path.Combine(projects.ActiveBookRoot!, "Characters", "writer.json");
        const string original = """{"id":"writer","sections":[{"id":"same","title":"First"},{"id":"same","title":"Second"}]}""";
        await File.WriteAllTextAsync(path, original, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidDataException>(() => new EntityService(projects).LoadCharactersAsync());

        Assert.Equal(original, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }
}
