using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Novalist.Sync.Server.Data;
using Novalist.Sync.Client;
using Npgsql;
using Xunit;

namespace Novalist.Sync.IntegrationTests;

public sealed class CommunityServerFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string Issuer = "https://novalist-integration.invalid";
    private const string Audience = "novalist-integration";
    private readonly SymmetricSecurityKey signing = new(RandomNumberGenerator.GetBytes(32));
    private readonly string connectionString = new NpgsqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("ConnectionStrings__Default")
        ?? throw new InvalidOperationException("Set ConnectionStrings__Default to an isolated local PostgreSQL database."))
    { Database = $"novalist_client_test_{Guid.NewGuid():N}" }.ConnectionString;

    private Uri? address;
    public Uri Address => address ?? throw new InvalidOperationException("The integration server has not started.");

    public async ValueTask InitializeAsync()
    {
        UseKestrel(0);
        using var client = CreateClient();
        address = client.BaseAddress ?? throw new InvalidOperationException("The integration server has no listening address.");
        Services.GetRequiredService<IConfiguration>()["Collaboration:ServerUrl"] = address.AbsoluteUri;
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SyncDbContext>().Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Relay:Enabled", "false");
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services => services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                options.Configuration.SigningKeys.Add(signing);
                options.TokenValidationParameters.ValidIssuer = Issuer;
                options.TokenValidationParameters.ValidAudience = Audience;
                options.TokenValidationParameters.IssuerSigningKey = signing;
            }));
    }

    public string Token(string subject, DeviceIdentity? boundDevice = null)
    {
        var claims = new Dictionary<string, object> { ["sub"] = subject };
        if (boundDevice is not null)
        {
            claims["amr"] = new[] { "mfa" };
            claims["auth_time"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            claims["cnf"] = new Dictionary<string, string> { ["jkt"] = HostedDeviceEnrollment.SigningKeyThumbprint(boundDevice) };
        }
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer, Audience = Audience, Claims = claims,
            Expires = DateTime.UtcNow.AddMinutes(10), SigningCredentials = new(signing, SecurityAlgorithms.HmacSha256)
        });
    }

    public async Task EnrollAsync(DeviceIdentity device, string subject, CancellationToken ct)
    {
        var recoveryKey = RandomNumberGenerator.GetBytes(32);
        try
        {
            using var recovery = RecoveryDeviceIdentity.Derive(recoveryKey);
            await HostedDeviceEnrollment.EnrollAsync(Address, device, recovery, Token(subject, device), ct);
        }
        finally { CryptographicOperations.ZeroMemory(recoveryKey); }
    }

    public async Task AddContributorAsync(Guid project, string subject, CancellationToken cancellationToken)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
        var user = await db.Users.SingleAsync(user => user.Issuer == Issuer && user.Subject == subject, cancellationToken);
        db.Memberships.Add(new Membership { ProjectId = project, UserId = user.Id, Role = MembershipRole.Contributor });
        await db.SaveChangesAsync(cancellationToken);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await using (var scope = Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<SyncDbContext>().Database.EnsureDeletedAsync();
        await base.DisposeAsync();
    }
}
