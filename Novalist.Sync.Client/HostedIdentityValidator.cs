using System.Security.Claims;
using System.Text.Json;
using Duende.IdentityModel.OidcClient;
using Duende.IdentityModel.OidcClient.Results;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Novalist.Sync.Client;

internal sealed class HostedIdentityValidator(string nonce, string? subject = null) : IIdentityTokenValidator
{
    public async Task<IdentityTokenValidationResult> ValidateAsync(string identityToken, OidcClientOptions options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(identityToken, new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = options.ProviderInformation.IssuerName,
            ValidateAudience = true,
            AudienceValidator = (audiences, _, _) => audiences is not null && audiences.SequenceEqual([options.ClientId]),
            ValidateLifetime = true, RequireExpirationTime = true, ClockSkew = TimeSpan.FromMinutes(1),
            RequireSignedTokens = true, ValidateIssuerSigningKey = true,
            ValidAlgorithms = options.Policy.ValidSignatureAlgorithms,
            IssuerSigningKeys = options.ProviderInformation.KeySet.Keys.Select(key => new JsonWebKey(JsonSerializer.Serialize(key)))
        });
        if (!validation.IsValid) return new() { Error = "invalid_identity_token" };
        var identity = validation.ClaimsIdentity;
        var nonces = identity.FindAll("nonce").ToArray();
        var subjects = identity.FindAll("sub").ToArray();
        if (nonces.Length > 1 || (nonces.Length == 0 && subject is null) ||
            (nonces.Length == 1 && nonces[0].Value != nonce) || subjects.Length != 1 || string.IsNullOrWhiteSpace(subjects[0].Value) ||
            (subject is not null && subjects[0].Value != subject))
            return new() { Error = "invalid_identity_token" };
        return new() { User = new ClaimsPrincipal(identity), SignatureAlgorithm = ((JsonWebToken)validation.SecurityToken).Alg };
    }
}
