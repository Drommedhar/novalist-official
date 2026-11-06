using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Novalist.Sync.Client;
using Novalist.Sync.Server.Data;
using NSubstitute;
using Xunit;

namespace Novalist.Sync.IntegrationTests;

public sealed class HostedProjectRotationFailureTests(CommunityServerFixture server) : IClassFixture<CommunityServerFixture>
{
    [Theory]
    [InlineData("prepare")]
    [InlineData("upload")]
    [InlineData("cancel")]
    public async Task FailedCheckpointReleasesTheReservationAndAllowsWritingToResume(string failure)
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(Path.GetTempPath(), "novalist-rotation-failure", Guid.NewGuid().ToString("N"));
        var credentials = Substitute.For<ICollaborationCredentials>();
        credentials.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));
        var subject = Guid.NewGuid().ToString();
        using var owner = await new DeviceIdentityStore(credentials).GetOrCreateAsync(new("https://identity.example"), subject, ct);
        await server.EnrollAsync(owner, subject, ct);
        try
        {
            using var access = await new HostedProjectAccessStore(credentials).CreateOwnerAsync(server.Address, owner, ct);
            await using var session = await HostedProjectSession.OpenAsync(folder, Guid.NewGuid(), access, owner,
                () => Task.FromResult(server.Token(subject)));
            await session.StartAsync(true, ct);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var error = await Assert.ThrowsAnyAsync<Exception>(() =>
                new HostedProjectRotation(folder, owner, session.Transport).RotateAsync(access, Guid.NewGuid(),
                    async (boundary, token) =>
                    {
                        await session.Updates.SynchronizeAsync(false, token);
                        Assert.Equal(boundary, session.Updates.LastServerSequence);
                        if (failure == "prepare") throw new IOException("Checkpoint preparation failed.");
                        if (failure == "cancel") { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
                        return new MemoryStream();
                    }, cancellation.Token));
            if (failure == "cancel") Assert.IsAssignableFrom<OperationCanceledException>(error);
            else if (failure == "prepare") Assert.IsType<IOException>(error);
            else Assert.IsType<InvalidDataException>(error);
            await using var scope = server.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
            Assert.False(await db.ProjectEpochStages.AnyAsync(stage => stage.ProjectId == access.ProjectId, ct));

            session.Updates.Save(Guid.NewGuid(), "Writing continues after failed rotation"u8.ToArray());
            await session.Updates.SynchronizeAsync(false, ct);
            Assert.True(session.Updates.LastServerSequence > 0);
            Assert.Equal(0U, (await session.Transport.GetProjectEpochAsync(access.ProjectId, ct)).Epoch);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
