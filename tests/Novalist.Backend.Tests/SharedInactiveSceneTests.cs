using System.Text;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Sync.Client.Tests;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class SharedInactiveSceneTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemoteWritingUpdatesWordCountsWhileAnotherBookOrDraftIsOpen(bool anotherDraft)
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var owner = new HostedDesktop(Path.Combine(directory.Path, "owner-settings"), identity);
        var project = await owner.Rpc.InvokeAsync<ProjectStateDto>("project/create", directory.Path, "Shared", "Book");
        var chapter = Assert.Single((await owner.Rpc.InvokeAsync<ProjectStateDto>("project/createChapter", "Opening")).Chapters);
        var scene = Assert.Single(Assert.Single((await owner.Rpc.InvokeAsync<ProjectStateDto>("project/createScene", chapter.Guid, "Arrival")).Chapters).Scenes);
        await owner.Rpc.InvokeAsync("scenes/write", chapter.Guid, scene.Id, "<p>Before.</p>", "Before.");
        var ownerRoot = project.ProjectPath ?? throw new InvalidOperationException("Missing project.");
        var relative = Path.GetRelativePath(ownerRoot, Assert.Single(Directory.GetFiles(ownerRoot, "*.novalist", SearchOption.AllDirectories)));
        var original = anotherDraft ? Assert.Single(await owner.Rpc.InvokeAsync<DraftDto[]>("project/drafts")).Id : project.ActiveBookId;
        var alternate = anotherDraft
            ? (await owner.Rpc.InvokeAsync<DraftDto[]>("project/createDraft", "Second draft", original)).Single(draft => draft.Id != original).Id
            : (await owner.Rpc.InvokeAsync<ProjectStateDto>("project/createBook", "Other book")).Books.Single(book => book.Id != original).Id;
        await owner.SignInAsync(server.Address);
        var session = await owner.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        var document = (await owner.Rpc.InvokeAsync<SceneContentDto>("scenes/read", chapter.Guid, scene.Id)).Shared ?? throw new InvalidOperationException("Missing shared scene.");
        var invitation = await owner.Rpc.InvokeAsync<string>("collaboration/invite", DateTimeOffset.UtcNow.AddHours(1));
        identity.Subject = "guest";
        await using var guest = new HostedDesktop(Path.Combine(directory.Path, "guest-settings"), identity);
        await guest.SignInAsync(server.Address);
        var guestRoot = Path.Combine(directory.Path, "Guest copy");
        await guest.Rpc.InvokeAsync("collaboration/join", server.Address, invitation, guestRoot);
        var switchMethod = anotherDraft ? "project/switchDraft" : "project/switchBook";
        await guest.Rpc.InvokeAsync<ProjectStateDto>(switchMethod, alternate);

        const string changed = "Four newly written words.";
        await owner.Rpc.InvokeAsync("collaboration/writeScene", session.SessionId, document.ItemId, Encoding.UTF8.GetBytes("<p>" + changed + "</p>"));
        for (var attempt = 0; attempt < 100 && !(await File.ReadAllTextAsync(Path.Combine(guestRoot, relative), ct)).Contains(changed, StringComparison.Ordinal); attempt++)
            await Task.Delay(20, ct);
        Assert.Contains(changed, await File.ReadAllTextAsync(Path.Combine(guestRoot, relative), ct));
        await guest.Rpc.InvokeAsync<ProjectStateDto>(switchMethod, original);
        Assert.Contains(changed, (await guest.Rpc.InvokeAsync<SceneContentDto>("scenes/read", chapter.Guid, scene.Id)).Html);

        var state = await guest.Rpc.InvokeAsync<ProjectStateDto>("project/getState");
        Assert.Equal(4, Assert.Single(Assert.Single(state.Chapters).Scenes).WordCount);
    }
}
