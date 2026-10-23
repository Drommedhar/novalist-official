using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Novalist.Sync.Client.Tests;

internal sealed class TestIdentityProvider : IAsyncDisposable
{
    private readonly RSA rsa = RSA.Create(2048);
    private readonly WebApplication app;
    private readonly HttpClient browser = new();
    private string challenge = string.Empty;
    private string nonce = string.Empty;
    private string redirect = string.Empty;
    public Uri Authority => new(app.Urls.Single());
    public bool ExchangedWithPkce { get; private set; }
    public int AccessLifetime { get; set; } = 300;
    public int Refreshes { get; private set; }
    public bool RejectRefresh { get; set; }
    public string? TokenFault { get; set; }
    public string Subject { get; set; } = "writer";
    public Func<string>? IssueAccessToken { get; set; }

    private TestIdentityProvider()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        app = builder.Build();
        app.MapGet("/client-configuration", () => new { protocolVersion = 1, authority = Authority.AbsoluteUri, clientId = "novalist", scope = "openid profile sync offline_access" });
        app.MapGet("/.well-known/openid-configuration", () => new
        {
            issuer = Authority.AbsoluteUri.TrimEnd('/'),
            authorization_endpoint = new Uri(Authority, "/authorize").AbsoluteUri,
            token_endpoint = new Uri(Authority, "/token").AbsoluteUri,
            jwks_uri = new Uri(Authority, "/keys").AbsoluteUri,
            response_types_supported = new[] { "code" }, subject_types_supported = new[] { "public" },
            id_token_signing_alg_values_supported = new[] { "RS256" }, code_challenge_methods_supported = new[] { "S256" }
        });
        app.MapGet("/keys", () =>
        {
            var parameters = rsa.ExportParameters(false);
            return new { keys = new[] { new { kty = "RSA", kid = "test", alg = "RS256", use = "sig",
                n = Base64UrlEncoder.Encode(parameters.Modulus ?? throw new CryptographicException("Missing RSA modulus.")),
                e = Base64UrlEncoder.Encode(parameters.Exponent ?? throw new CryptographicException("Missing RSA exponent.")) } } };
        });
        app.MapGet("/authorize", Authorize);
        app.MapPost("/token", (Func<HttpContext, Task<IResult>>)TokenAsync);
    }

    public static async Task<TestIdentityProvider> StartAsync(CancellationToken ct)
    {
        var provider = new TestIdentityProvider();
        try { await provider.app.StartAsync(ct); return provider; }
        catch { await provider.DisposeAsync(); throw; }
    }

    public async Task OpenBrowserAsync(Uri url, CancellationToken ct)
    {
        using var result = await browser.GetAsync(url, ct);
        result.EnsureSuccessStatusCode();
    }

    public Task StopAsync(CancellationToken ct) => app.StopAsync(ct);

    private IResult Authorize(HttpContext context)
    {
        var query = context.Request.Query;
        if (query["client_id"] != "novalist" || query["response_type"] != "code" || query["code_challenge_method"] != "S256") return Results.BadRequest();
        challenge = query["code_challenge"].ToString();
        nonce = query["nonce"].ToString();
        redirect = query["redirect_uri"].ToString();
        if (!Uri.TryCreate(redirect, UriKind.Absolute, out var callback) || callback.Host != "127.0.0.1") return Results.BadRequest();
        return Results.Redirect(redirect + "?code=code&state=" + Uri.EscapeDataString(query["state"].ToString()));
    }

    private async Task<IResult> TokenAsync(HttpContext context)
    {
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        if (form["grant_type"] == "refresh_token")
        {
            var expected = Refreshes == 0 ? "refresh" : $"rotated-{Refreshes}";
            if (RejectRefresh || form["refresh_token"] != expected || form["client_id"] != "novalist") return Results.BadRequest();
            Refreshes++;
            return Results.Json(new { access_token = IssueAccessToken?.Invoke() ?? $"renewed-{Refreshes}", refresh_token = $"rotated-{Refreshes}", token_type = "Bearer", expires_in = 300 });
        }
        ExchangedWithPkce = challenge == Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"].ToString())));
        if (!ExchangedWithPkce || form["code"] != "code" || form["redirect_uri"] != redirect || form["client_id"] != "novalist") return Results.BadRequest();
        return Results.Json(new { access_token = IssueAccessToken?.Invoke() ?? "access", refresh_token = "refresh", id_token = IdentityToken(), token_type = "Bearer", expires_in = AccessLifetime });
    }

    private string? IdentityToken()
    {
        if (TokenFault == "missing") return null;
        using var otherKey = RSA.Create(2048);
        var claims = new Dictionary<string, object> { ["sub"] = TokenFault == "empty-subject" ? "" : Subject, ["name"] = "Writer", ["nonce"] = TokenFault == "nonce" ? "wrong" : nonce };
        if (TokenFault == "missing-nonce") claims.Remove("nonce");
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = TokenFault == "issuer" ? "https://wrong.example" : Authority.AbsoluteUri.TrimEnd('/'),
            Audience = TokenFault == "audience" ? "wrong-client" : "novalist",
            IssuedAt = DateTime.UtcNow.AddMinutes(-15), NotBefore = DateTime.UtcNow.AddMinutes(-15),
            Expires = TokenFault == "expired" ? DateTime.UtcNow.AddMinutes(-10) : DateTime.UtcNow.AddMinutes(5), Claims = claims,
            SigningCredentials = TokenFault == "unsigned" ? null : new(new RsaSecurityKey(TokenFault == "signature" ? otherKey : rsa) { KeyId = "test" }, SecurityAlgorithms.RsaSha256)
        };
        if (TokenFault == "extra-audience") descriptor.Audiences.Add("untrusted-client");
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public async ValueTask DisposeAsync()
    {
        await app.DisposeAsync();
        browser.Dispose();
        rsa.Dispose();
    }
}
