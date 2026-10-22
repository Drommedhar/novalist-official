using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Novalist.Sync.IntegrationTests;

public sealed class RelayServerFixture : WebApplicationFactory<Program>
{
    private readonly SymmetricSecurityKey signing = new(RandomNumberGenerator.GetBytes(32));
    public Uri Address { get; }

    public RelayServerFixture()
    {
        UseKestrel(0);
        using var client = CreateClient();
        Address = client.BaseAddress!;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Relay:Enabled", "true");
        builder.UseSetting("ConnectionStrings:Default", "");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services => services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.Configuration = new OpenIdConnectConfiguration { Issuer = "https://relay.invalid" };
                options.Configuration.SigningKeys.Add(signing);
                options.TokenValidationParameters.ValidIssuer = "https://relay.invalid";
                options.TokenValidationParameters.ValidAudience = "relay-test";
                options.TokenValidationParameters.IssuerSigningKey = signing;
            }));
    }

    public string Token(string subject) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = "https://relay.invalid", Audience = "relay-test",
        Claims = new Dictionary<string, object> { ["sub"] = subject },
        Expires = DateTime.UtcNow.AddMinutes(10),
        SigningCredentials = new(signing, SecurityAlgorithms.HmacSha256)
    });
}
