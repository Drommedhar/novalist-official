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

    public string Token(string subject) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer,
        Audience = Audience,
        Claims = new Dictionary<string, object> { ["sub"] = subject },
        Expires = DateTime.UtcNow.AddMinutes(10),
        SigningCredentials = new(signing, SecurityAlgorithms.HmacSha256)
    });

    public void ConfigureDesktopSignIn(Uri authority)
    {
        var configuration = Services.GetRequiredService<IConfiguration>();
        Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme).Authority = authority.AbsoluteUri;
        configuration["Authentication:NativeClient:ClientId"] = "novalist";
        configuration["Authentication:NativeClient:Scope"] = "openid profile sync offline_access";
    }

    public async Task AddContributorAsync(Guid project, string subject, CancellationToken cancellationToken)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
        var user = await db.Users.SingleAsync(user => user.Issuer == Issuer && user.Subject == subject, cancellationToken);
        db.Memberships.Add(new Membership { ProjectId = project, UserId = user.Id, Role = MembershipRole.Contributor });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<InvitationBlock> HoldInvitationsAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand("BEGIN; LOCK TABLE invitations IN ACCESS EXCLUSIVE MODE", connection);
            await command.ExecuteNonQueryAsync(ct);
            return new(connection);
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    public sealed class InvitationBlock(NpgsqlConnection connection) : IAsyncDisposable
    {
        public async Task WaitForBlockedRequestAsync(CancellationToken ct)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            await using var command = new NpgsqlCommand("""
                SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname = current_database()
                AND pid <> pg_backend_pid() AND wait_event_type = 'Lock' AND query ILIKE '%invitations%')
                """, connection);
            await using var refresh = new NpgsqlCommand("SELECT pg_stat_clear_snapshot()", connection);
            while (true)
            {
                await refresh.ExecuteNonQueryAsync(deadline.Token);
                if (Equals(await command.ExecuteScalarAsync(deadline.Token), true)) return;
                await Task.Delay(20, deadline.Token);
            }
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await using (var scope = Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<SyncDbContext>().Database.EnsureDeletedAsync();
        await base.DisposeAsync();
    }
}
