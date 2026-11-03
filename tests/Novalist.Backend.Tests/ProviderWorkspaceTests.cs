using Novalist.Core.Services;
using Novalist.Backend.Rpc;
using NSubstitute;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class ProviderWorkspaceTests
{
    [Fact]
    public async Task CoversUseProviderBytesWithoutRequiringLocalFiles()
    {
        var files = Substitute.For<IFileService>();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "cover.png");
        files.ExistsAsync(path).Returns(true);
        files.ReadBytesAsync(path).Returns([1, 2, 3]);

        Assert.Equal("data:image/png;base64,AQID", await Workspace.LoadCoverDataUriAsync(path, files));
    }

    [Fact]
    public async Task AnUnavailableProviderCoverDoesNotBreakTheBookshelf()
    {
        var files = Substitute.For<IFileService>();
        files.ExistsAsync("cover.png").Returns(Task.FromException<bool>(new UnauthorizedAccessException()));

        Assert.Null(await Workspace.LoadCoverDataUriAsync("cover.png", files));
    }

    [Fact]
    public async Task BackupsUseTheArchiveServiceSuppliedByTheHost()
    {
        using var temp = new TestHelpers.TempDir();
        var archive = Substitute.For<IArchiveService>();
        using var workspace = new Workspace(Path.Combine(temp.Path, "settings"), archiveService: archive);
        await workspace.Projects.CreateProjectAsync(temp.Path, "Novel", "Book");
        workspace.Settings.Settings.BackupFolder = Path.Combine(temp.Path, "backups");
        archive.CreateFromDirectoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>())
            .Returns(async call =>
            {
                await File.WriteAllBytesAsync(call.ArgAt<string>(1), [1]);
                return 1;
            });

        Assert.NotNull(await new BackupRpc(workspace).CreateMilestoneAsync("Checkpoint"));

        await archive.Received(1).CreateFromDirectoryAsync(workspace.Projects.ProjectRoot!,
            Arg.Any<string>(), Arg.Is<IReadOnlyCollection<string>>(excluded => excluded.Contains(".git")));
    }
}
