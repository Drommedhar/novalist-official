using System.Text.Json;
using NSubstitute;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed partial class EntitiesRpcReadTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly IFileService _files = Substitute.For<IFileService, IBatchTextReader>();
    private readonly List<string> _reads = [];
    private readonly Workspace _workspace;
    private readonly EntitiesRpc _rpc;

    public EntitiesRpcReadTests()
    {
        var local = new FileService();
        ConfigureBatchReads(local);
        _files.CombinePath(Arg.Any<string[]>()).Returns(call => Path.Combine(call.Arg<string[]>()));
        _files.GetFileName(Arg.Any<string>()).Returns(call => Path.GetFileName(call.Arg<string>()));
        _files.GetDirectoryName(Arg.Any<string>()).Returns(call => Path.GetDirectoryName(call.Arg<string>()) ?? "");
        _files.ReadTextAsync(Arg.Any<string>()).Returns(call =>
        {
            var path = call.Arg<string>();
            _reads.Add(path);
            return local.ReadTextAsync(path);
        });
        _files.WriteTextAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(call => local.WriteTextAsync(call.ArgAt<string>(0), call.ArgAt<string>(1)));
        _files.CreateDirectoryAsync(Arg.Any<string>()).Returns(call => local.CreateDirectoryAsync(call.Arg<string>()));
        _files.DirectoryExistsAsync(Arg.Any<string>()).Returns(call => local.DirectoryExistsAsync(call.Arg<string>()));
        _files.GetFilesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()).Returns(call => local.GetFilesAsync(call.ArgAt<string>(0), call.ArgAt<string>(1), call.ArgAt<bool>(2)));
        _files.GetDirectoriesAsync(Arg.Any<string>()).Returns(call => local.GetDirectoriesAsync(call.Arg<string>()));
        _files.ExistsAsync(Arg.Any<string>()).Returns(call => local.ExistsAsync(call.Arg<string>()));
        _workspace = new Workspace(_temp.Combine("settings"), fileService: _files);
        _workspace.Projects.CreateProjectAsync(_temp.Path, "Novel", "Book").GetAwaiter().GetResult();
        _rpc = new EntitiesRpc(_workspace);
    }

    [Theory]
    [InlineData("character", false)]
    [InlineData("location", false)]
    [InlineData("item", false)]
    [InlineData("lore", false)]
    [InlineData("guild", false)]
    [InlineData("character", true)]
    [InlineData("location", true)]
    [InlineData("item", true)]
    [InlineData("lore", true)]
    [InlineData("guild", true)]
    public async Task GetReadsOnlySelectedEntryFromLargeCodex(string type, bool listFirst)
    {
        PopulateCodex(type);
        if (listFirst) await _rpc.ListAsync(type);
        _reads.Clear();

        var selected = await _rpc.GetAsync(type, "entry-24");

        Assert.Equal("Entry 24", selected.GetProperty("name").GetString());
        Assert.Single(_reads);
    }

    public static IEnumerable<object[]> DetailReads()
        => from type in new[] { "character", "location", "item", "lore", "guild" }
           from detail in new[] { "properties", "match", "ai", "reader", "state", "attachments" }
           select new object[] { type, detail };

    [Theory]
    [MemberData(nameof(DetailReads))]
    public async Task SelectedDetailReadsOnlyItsEntry(string type, string detail)
    {
        PopulateCodex(type);
        WriteDetailedEntry(type);
        _reads.Clear();

        switch (detail)
        {
            case "properties":
                Assert.Equal("Keep watch", Assert.Single(await _rpc.GetCustomPropsAsync(type, "entry-24")).Value);
                break;
            case "match":
                Assert.True((await _rpc.GetMatchSettingsAsync(type, "entry-24")).CaseSensitive);
                break;
            case "ai":
                Assert.Equal("Never", (await _rpc.GetAiPolicyAsync(type, "entry-24")).Inclusion);
                break;
            case "reader":
                Assert.True((await _rpc.GetReaderPolicyAsync(type, "entry-24")).Hidden);
                break;
            case "state":
                Assert.Equal("Changed", Assert.Single(await _rpc.GetStateOverridesAsync(type, "entry-24")).Name);
                break;
            case "attachments":
                Assert.Equal("Research", Assert.Single(await new AttachmentsRpc(_workspace).ListAsync(type, "entry-24")).Name);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(detail));
        }

        Assert.Single(_reads);
    }

    [Fact]
    public async Task SelectedArcReadsOnlyItsCharacter()
    {
        PopulateCodex("character");
        WriteDetailedEntry("character");
        _reads.Clear();

        var arc = await new ArcRpc(_workspace).GetAsync("entry-24");

        Assert.Equal("Afraid", arc.Start);
        Assert.Single(_reads);
    }

    [Theory]
    [InlineData("fields")]
    [InlineData("remove")]
    [InlineData("images")]
    public async Task SelectedOverrideDoesNotReadOtherCharacters(string operation)
    {
        PopulateCodex("character");
        WriteDetailedEntry("character");
        _reads.Clear();

        var updated = operation switch
        {
            "fields" => await _rpc.SetOverrideAsync("entry-24", "chapter", null, new() { ["eyeColor"] = "grey" }),
            "remove" => await _rpc.RemoveOverrideAsync("entry-24", "chapter", null),
            "images" => await _rpc.SetOverrideImagesAsync("entry-24", "chapter", null, []),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        Assert.Equal("entry-24", updated.GetProperty("id").GetString());
        Assert.Equal(2, _reads.Count);
        Assert.Single(_reads.Distinct());
    }

    private void PopulateCodex(string type)
    {
        _workspace.Projects.CurrentProject!.CustomEntityTypes.Add(new CustomEntityTypeDefinition
        {
            TypeKey = "guild", FolderName = "Guilds"
        });
        for (var index = 0; index < 48; index++)
            WriteEntry(type, $"entry-{index}", $"Entry {index}");
    }

    private void WriteDetailedEntry(string type)
    {
        var path = WriteEntry(type, "entry-24", "Selected");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            id = "entry-24", name = "Selected", entityTypeKey = type,
            customProperties = new Dictionary<string, string> { ["Motto"] = "Keep watch" },
            match = new EntityMatchSettings { CaseSensitive = true },
            ai = AiInclusion.Never, readerHidden = true,
            stateOverrides = new[] { new EntityStateOverride { Name = "Changed", Chapter = "chapter" } },
            attachments = new[] { new EntityAttachment { Name = "Research", Kind = AttachmentKind.Link, Url = "https://example.com" } },
            arc = new CharacterArc { Start = "Afraid" }
        }));
    }

    [Fact]
    public async Task GetPrefersRenamedBookEntryOverWorldBibleEntry()
    {
        WriteEntry("character", "shared", "Book", filename: "renamed.json");
        WriteEntry("character", "shared", "World", worldBible: true);

        var selected = await _rpc.GetAsync("character", "shared");

        Assert.Equal("Book", selected.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("null")]
    [InlineData("{\"id\":\"someone-else\",\"name\":\"Other\"}")]
    public async Task GetFindsRenamedEntryWhenCanonicalFileDoesNotMatch(string canonicalJson)
    {
        var canonical = WriteEntry("character", "selected", "Replaced");
        File.WriteAllText(canonical, canonicalJson);
        WriteEntry("character", "selected", "Renamed", filename: "external-name.json");

        var selected = await _rpc.GetAsync("character", "selected");

        Assert.Equal("Renamed", selected.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetReadsExternalEditsAfterListing(bool rename)
    {
        var path = WriteEntry("character", "selected", "Before");
        await _rpc.ListAsync("character");
        WriteEntry("character", "selected", "Edited", filename: rename ? "renamed.json" : null);
        if (rename) File.Delete(path);

        var selected = await _rpc.GetAsync("character", "selected");

        Assert.Equal("Edited", selected.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetFallsBackToWorldBibleAfterBookEntryIsRemoved()
    {
        var path = WriteEntry("character", "selected", "Book");
        WriteEntry("character", "selected", "World", worldBible: true, filename: "renamed.json");
        await _rpc.ListAsync("character");
        File.Delete(path);

        var selected = await _rpc.GetAsync("character", "selected");

        Assert.Equal("World", selected.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetSurfacesUnavailableSelectedFile(bool accessDenied)
    {
        var path = WriteEntry("character", "selected", "Book");
        WriteEntry("character", "selected", "World", worldBible: true);
        Exception failure = accessDenied
            ? new UnauthorizedAccessException("Access denied")
            : new IOException("Download unavailable");
        _files.ReadTextAsync(path).Returns(_ => Task.FromException<string>(failure));

        await Assert.ThrowsAsync(failure.GetType(), () => _rpc.GetAsync("character", "selected"));
    }

    [Fact]
    public async Task GetSkipsMalformedBookEntryWhenWorldBibleEntryIsValid()
    {
        var path = WriteEntry("character", "selected", "Book");
        File.WriteAllText(path, "{ broken");
        WriteEntry("character", "selected", "World", worldBible: true);

        var selected = await _rpc.GetAsync("character", "selected");
        var entity = await new EntityService(_workspace.Projects).LoadCharacterAsync("selected");

        Assert.Equal("World", selected.GetProperty("name").GetString());
        Assert.NotNull(entity);
        Assert.True(entity.IsWorldBible);
    }

    [Fact]
    public async Task GetDoesNotTreatIdAsPathOutsideEntityDirectory()
    {
        var path = Path.Combine(_workspace.Projects.ActiveBookRoot!, "outside.json");
        File.WriteAllText(path, "{\"id\":\"../outside\",\"name\":\"Outside\"}");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.GetAsync("character", "../outside"));

        Assert.DoesNotContain(path, _reads);
    }

    private string WriteEntry(string type, string id, string name, bool worldBible = false, string? filename = null)
    {
        var book = _workspace.Projects.ActiveBook!;
        var project = _workspace.Projects.CurrentProject!;
        var folder = type switch
        {
            "character" => worldBible ? project.CharacterFolder : book.CharacterFolder,
            "location" => worldBible ? project.LocationFolder : book.LocationFolder,
            "item" => worldBible ? project.ItemFolder : book.ItemFolder,
            "lore" => worldBible ? project.LoreFolder : book.LoreFolder,
            _ => "Guilds"
        };
        var root = worldBible ? _workspace.Projects.WorldBibleRoot! : _workspace.Projects.ActiveBookRoot!;
        var path = Path.Combine(root, folder, filename ?? id + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { id, name, entityTypeKey = type }));
        return path;
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _temp.Dispose();
    }
}
