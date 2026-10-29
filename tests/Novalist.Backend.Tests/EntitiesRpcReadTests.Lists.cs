using NSubstitute;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed partial class EntitiesRpcReadTests
{
    private readonly List<string[]> _batches = [];

    private void ConfigureBatchReads(IFileService local)
    {
        ((IBatchTextReader)_files).ReadTextsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => ReadBatchAsync(local, call.Arg<IReadOnlyList<string>>(), call.Arg<CancellationToken>()));
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadBatchAsync(
        IFileService local, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        _batches.Add(paths.ToArray());
        var result = new Dictionary<string, string>();
        foreach (var path in paths.Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(path, await local.ReadTextAsync(path));
        }
        return result;
    }

    [Theory]
    [InlineData("character")]
    [InlineData("location")]
    [InlineData("item")]
    [InlineData("lore")]
    [InlineData("guild")]
    public async Task ListBatchesEachFolderAndPreservesBookOverridesAndSourceOrder(string type)
    {
        _workspace.Projects.CurrentProject!.CustomEntityTypes.Add(new CustomEntityTypeDefinition
        {
            TypeKey = "guild", FolderName = "Guilds"
        });
        var book = WriteEntry(type, "shared", "Book");
        var local = WriteEntry(type, "local", "Local");
        var broken = WriteEntry(type, "broken", "Broken");
        File.WriteAllText(broken, "{ broken");
        var overridden = WriteEntry(type, "shared", "Overridden", worldBible: true);
        var world = WriteEntry(type, "world", "World", worldBible: true);
        var worldBroken = WriteEntry(type, "broken", "Broken", worldBible: true);
        File.WriteAllText(worldBroken, "null");
        _files.GetFilesAsync(Path.GetDirectoryName(book)!, "*.json").Returns(new[] { local, broken, book });
        _files.GetFilesAsync(Path.GetDirectoryName(world)!, "*.json").Returns(new[] { overridden, worldBroken, world });
        _batches.Clear();

        var entries = await _rpc.ListAsync(type);

        Assert.Equal(new[] { "Local", "Book", "World" }, entries.Select(entry => entry.Name));
        Assert.Equal(new[] { false, false, true }, entries.Select(entry => entry.IsWorldBible));
        Assert.Equal(2, _batches.Count);
        Assert.Equal(new[] { local, broken, book }, _batches[0]);
        Assert.Equal(new[] { overridden, worldBroken, world }, _batches[1]);
    }

    [Fact]
    public async Task CancelledListDoesNotStartReadingEntries()
    {
        PopulateCodex("character");
        _reads.Clear();
        _batches.Clear();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _rpc.ListAsync("character", cancellation.Token));

        Assert.Empty(_reads);
        Assert.Empty(_batches);
    }

    [Fact]
    public async Task ListPassesCancellationToProviderWithoutStartingWorldBibleBatch()
    {
        WriteEntry("character", "book", "Book");
        WriteEntry("character", "world", "World", worldBible: true);
        using var cancellation = new CancellationTokenSource();
        ((IBatchTextReader)_files).ReadTextsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Assert.Equal(cancellation.Token, call.Arg<CancellationToken>());
                _batches.Add(call.Arg<IReadOnlyList<string>>().ToArray());
                cancellation.Cancel();
                return Task.FromCanceled<IReadOnlyDictionary<string, string>>(cancellation.Token);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _rpc.ListAsync("character", cancellation.Token));

        Assert.Single(_batches);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListSurfacesBatchReadFailures(bool accessDenied)
    {
        WriteEntry("character", "book", "Book");
        Exception failure = accessDenied
            ? new UnauthorizedAccessException("Access denied")
            : new IOException("Download unavailable");
        ((IBatchTextReader)_files).ReadTextsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<IReadOnlyDictionary<string, string>>(failure));

        await Assert.ThrowsAsync(failure.GetType(), () => _rpc.ListAsync("character"));
    }

    [Theory]
    [InlineData("character")]
    [InlineData("location")]
    [InlineData("item")]
    [InlineData("lore")]
    public async Task SpellDictionaryPassesCancellationToCodexReads(string type)
    {
        PopulateCodex(type);
        using var cancellation = new CancellationTokenSource();
        ((IBatchTextReader)_files).ReadTextsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Assert.Equal(cancellation.Token, call.Arg<CancellationToken>());
                cancellation.Cancel();
                return Task.FromCanceled<IReadOnlyDictionary<string, string>>(cancellation.Token);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SpellRpc(_workspace).GetDictionaryAsync(cancellation.Token));
    }

    [Theory]
    [InlineData("character")]
    [InlineData("location")]
    public async Task DashboardPassesCancellationToCodexReads(string type)
    {
        PopulateCodex(type);
        using var cancellation = new CancellationTokenSource();
        ((IBatchTextReader)_files).ReadTextsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Assert.Equal(cancellation.Token, call.Arg<CancellationToken>());
                cancellation.Cancel();
                return Task.FromCanceled<IReadOnlyDictionary<string, string>>(cancellation.Token);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DashboardRpc(_workspace).GetAsync(1, cancellationToken: cancellation.Token));
    }

    [Theory]
    [InlineData("character")]
    [InlineData("location")]
    [InlineData("item")]
    [InlineData("lore")]
    [InlineData("guild")]
    public async Task GroupsPassCancellationThroughEveryEntityType(string type)
    {
        PopulateCodex(type);
        _batches.Clear();
        using var cancellation = new CancellationTokenSource();
        ((IBatchTextReader)_files).ReadTextsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Assert.Equal(cancellation.Token, call.Arg<CancellationToken>());
                _batches.Add(call.Arg<IReadOnlyList<string>>().ToArray());
                cancellation.Cancel();
                return Task.FromCanceled<IReadOnlyDictionary<string, string>>(cancellation.Token);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _rpc.GroupsAsync(cancellation.Token));

        Assert.Single(_batches);
    }

    [Theory]
    [InlineData("character")]
    [InlineData("location")]
    [InlineData("item")]
    [InlineData("lore")]
    [InlineData("guild")]
    public async Task RelationshipSuggestionsPassCancellationThroughEveryEntityType(string type)
    {
        PopulateCodex(type);
        using var cancellation = new CancellationTokenSource();
        ((IBatchTextReader)_files).ReadTextsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Assert.Equal(cancellation.Token, call.Arg<CancellationToken>());
                cancellation.Cancel();
                return Task.FromCanceled<IReadOnlyDictionary<string, string>>(cancellation.Token);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _rpc.RelationshipSuggestionsAsync(cancellation.Token));
    }

    [Theory]
    [InlineData("location")]
    [InlineData("item")]
    [InlineData("lore")]
    public async Task RelationshipSuggestionsReadEachTypeOnlyOnce(string type)
    {
        PopulateCodex(type);
        _batches.Clear();

        var suggestions = await _rpc.RelationshipSuggestionsAsync();

        Assert.Equal(48, suggestions.CharacterNames.Count);
        Assert.Single(_batches, paths => paths.Length > 0);
        Assert.Equal(48, _batches.Sum(paths => paths.Length));
    }
}
