using System.Security.Cryptography;
using System.Net.Http.Json;
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
    private readonly bool requireAuthentication;

    public RelayServerFixture(bool requireAuthentication = true)
    {
        this.requireAuthentication = requireAuthentication;
        UseKestrel(0);
        using var client = CreateClient();
        Address = client.BaseAddress!;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", "");
        builder.UseSetting("Relay:RequireAuthentication", requireAuthentication.ToString());
        builder.UseSetting("Relay:Introspection:Endpoint", "https://relay.invalid/introspect");
        builder.UseSetting("Relay:Introspection:ClientId", "relay-integration");
        builder.UseSetting("Relay:Introspection:ClientSecret", "relay-integration-test-credential");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services => services.AddHttpClient("RelayIntrospection")
            .ConfigurePrimaryHttpMessageHandler(() => new IntrospectionHandler()));
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
        Issuer = "https://relay.invalid",
        Audience = "relay-test",
        Claims = new Dictionary<string, object> { ["sub"] = subject },
        Expires = DateTime.UtcNow.AddMinutes(10),
        SigningCredentials = new(signing, SecurityAlgorithms.HmacSha256)
    });

    private sealed class IntrospectionHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { active = true })
            });
    }
}
