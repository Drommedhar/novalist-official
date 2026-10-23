using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class RelayServerDiscoveryTests
{
    [Fact]
    public async Task LegacyConfigurationRequiresSignIn()
    {
        await using var server = await TestIdentityProvider.StartAsync(CancellationToken.None);
        var configuration = await RelayServerDiscovery.DiscoverAsync(server.Authority, CancellationToken.None);
        Assert.True(configuration.AuthenticationRequired);
        Assert.Equal(server.Authority, configuration.SignIn?.Authority);
    }

    [Fact]
    public async Task ExplicitAnonymousConfigurationNeedsNoIdentityProvider()
    {
        await using var server = await TestIdentityProvider.StartAsync(CancellationToken.None);
        server.ClientConfigurationJson = """{"protocolVersion":1,"authenticationRequired":false}""";
        var configuration = await RelayServerDiscovery.DiscoverAsync(server.Authority, CancellationToken.None);
        Assert.False(configuration.AuthenticationRequired);
        Assert.Null(configuration.SignIn);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"protocolVersion\":1}")]
    [InlineData("{\"protocolVersion\":1,\"authenticationRequired\":true}")]
    [InlineData("{\"protocolVersion\":1,\"authenticationRequired\":\"false\"}")]
    [InlineData("{\"protocolVersion\":2,\"authenticationRequired\":false}")]
    public async Task InvalidConfigurationCannotEnableAnonymousAccess(string json)
    {
        await using var server = await TestIdentityProvider.StartAsync(CancellationToken.None);
        server.ClientConfigurationJson = json;
        await Assert.ThrowsAnyAsync<Exception>(() => RelayServerDiscovery.DiscoverAsync(server.Authority, CancellationToken.None));
    }

    [Fact]
    public async Task MissingEndpointCannotEnableAnonymousAccess()
    {
        await using var server = await TestIdentityProvider.StartAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RelayServerDiscovery.DiscoverAsync(new Uri(server.Authority, "/missing"), CancellationToken.None));
    }
}
