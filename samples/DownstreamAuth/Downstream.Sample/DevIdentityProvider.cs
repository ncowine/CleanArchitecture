using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Downstream.Sample;

/// <summary>
/// A deliberately small OIDC/OAuth2 token issuer for local development — just enough of a real provider for
/// the CleanArch API to use it through the same configuration it would use for Okta:
/// <list type="bullet">
/// <item><c>/.well-known/openid-configuration</c> and <c>/.well-known/jwks.json</c> — so the API's JWT
/// handler (<c>Okta:Authority</c>) discovers the issuer and verifies signatures the standard way.</item>
/// <item><c>POST /connect/token</c> — the RFC 8693 token-exchange grant (On-Behalf-Of) and the
/// <c>client_credentials</c> grant, with the client authenticated by HTTP Basic (client_secret_basic).</item>
/// <item><c>GET /dev/token?user=alice</c> — stands in for an interactive login: a user access token for the
/// API, to paste into Swagger's Authorize dialog.</item>
/// </list>
/// NOT a real identity provider: no users, no consent, no refresh tokens, and a signing key that is generated
/// at startup and lost on restart (the API's JWT handler re-fetches the key set when it sees an unknown kid).
/// </summary>
internal sealed class DevIdentityProvider
{
    private const string TokenExchangeGrant = "urn:ietf:params:oauth:grant-type:token-exchange";
    private const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";

    private static readonly JsonWebTokenHandler TokenHandler = new();
    private static readonly string[] GrantTypes = [TokenExchangeGrant, "client_credentials"];
    private static readonly string[] ClientAuthMethods = ["client_secret_basic"];
    private static readonly string[] SigningAlgorithms = [SecurityAlgorithms.RsaSha256];

    private readonly IConfiguration _configuration;
    private readonly RsaSecurityKey _signingKey;
    private readonly SigningCredentials _signingCredentials;
    private readonly TimeSpan _lifetime;

    public DevIdentityProvider(IConfiguration configuration)
    {
        _configuration = configuration;
        Issuer = configuration["Issuer"] ?? throw new InvalidOperationException("DevIdp:Issuer is not set.");
        UserTokenAudience = configuration["UserTokenAudience"] ?? "cleanarch-api";
        _lifetime = TimeSpan.FromMinutes(configuration.GetValue("TokenLifetimeMinutes", 10));

        _signingKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = Guid.NewGuid().ToString("N") };
        _signingCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256);
    }

    public string Issuer { get; }

    public string UserTokenAudience { get; }

    /// <summary>The key the downstream endpoints in this process validate with (no discovery round-trip).</summary>
    public SecurityKey SigningKey => _signingKey;

    public void Map(WebApplication app)
    {
        app.MapGet("/.well-known/openid-configuration", () => Results.Json(new
        {
            issuer = Issuer,
            jwks_uri = $"{Issuer}/.well-known/jwks.json",
            token_endpoint = $"{Issuer}/connect/token",
            grant_types_supported = GrantTypes,
            token_endpoint_auth_methods_supported = ClientAuthMethods,
            id_token_signing_alg_values_supported = SigningAlgorithms,
        }));

        // Only the PUBLIC half of the key is published: built from the exported public parameters, so the
        // private exponent can never end up in the JWK.
        app.MapGet("/.well-known/jwks.json", () =>
        {
            var publicKey = new RsaSecurityKey(_signingKey.Rsa.ExportParameters(includePrivateParameters: false))
            {
                KeyId = _signingKey.KeyId,
            };
            var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(publicKey);
            return Results.Json(new
            {
                keys = new[] { new { kty = jwk.Kty, use = "sig", alg = SecurityAlgorithms.RsaSha256, kid = jwk.Kid, n = jwk.N, e = jwk.E } },
            });
        });

        app.MapPost("/connect/token", HandleTokenRequestAsync);

        app.MapGet("/dev/token", (string? user) =>
        {
            var subject = string.IsNullOrWhiteSpace(user) ? "alice" : user;
            var token = Mint(subject, UserTokenAudience, scope: "openid", extraClaims: new() { ["name"] = subject });
            return Results.Json(new
            {
                access_token = token,
                token_type = "Bearer",
                expires_in = (int)_lifetime.TotalSeconds,
                note = $"A user token for '{subject}' with aud={UserTokenAudience}. Paste access_token into " +
                       "the CleanArch API's Swagger Authorize dialog (Bearer), then call GET /demo/downstreams.",
            });
        });
    }

    private async Task<IResult> HandleTokenRequestAsync(HttpRequest request)
    {
        if (!request.HasFormContentType)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request", "Expected a form-encoded body.");
        }

        var form = await request.ReadFormAsync();

        var clientId = AuthenticateClient(request);
        if (clientId is null)
        {
            return Error(StatusCodes.Status401Unauthorized, "invalid_client", "Unknown client or wrong secret.");
        }

        var audience = form["audience"].ToString();
        var scope = form["scope"].ToString();
        var allowed = _configuration.GetSection($"Clients:{clientId}:Audiences").Get<string[]>() ?? [];
        if (!allowed.Contains(audience, StringComparer.Ordinal))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_target",
                $"Client '{clientId}' may not request a token for audience '{audience}'.");
        }

        switch (form["grant_type"].ToString())
        {
            case TokenExchangeGrant:
                // The subject token must be one THIS issuer minted for the requesting client — a user's token
                // for the API. Anything else (expired, forged, meant for another service) is invalid_grant.
                var subject = await TokenHandler.ValidateTokenAsync(form["subject_token"].ToString(), new TokenValidationParameters
                {
                    ValidIssuer = Issuer,
                    ValidAudience = clientId,
                    IssuerSigningKey = _signingKey,
                });
                if (!subject.IsValid)
                {
                    return Error(StatusCodes.Status400BadRequest, "invalid_grant",
                        $"The subject token was rejected: {subject.Exception?.Message}");
                }

                // Same user, new audience — and an 'act' claim (RFC 8693 §4.1) recording that the API is the
                // party acting on the user's behalf, so the downstream can see both.
                var exchanged = Mint(
                    (string)subject.Claims["sub"], audience, scope,
                    new() { ["act"] = new Dictionary<string, object> { ["sub"] = clientId }, ["client_id"] = clientId });
                return TokenResponse(exchanged, issuedTokenType: AccessTokenType);

            case "client_credentials":
                // No user: the token's subject is the client itself.
                return TokenResponse(Mint(clientId, audience, scope, new() { ["client_id"] = clientId }), null);

            default:
                return Error(StatusCodes.Status400BadRequest, "unsupported_grant_type",
                    "Supported: token exchange (RFC 8693) and client_credentials.");
        }
    }

    /// <summary>client_secret_basic: the client id and secret in an HTTP Basic header.</summary>
    private string? AuthenticateClient(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..].Trim()));
        }
        catch (FormatException)
        {
            return null;
        }

        var separator = decoded.IndexOf(':', StringComparison.Ordinal);
        if (separator < 0)
        {
            return null;
        }

        var clientId = decoded[..separator];
        var expected = _configuration[$"Clients:{clientId}:Secret"];
        return expected is not null && FixedTimeEquals(decoded[(separator + 1)..], expected) ? clientId : null;
    }

    private string Mint(string subject, string audience, string? scope, Dictionary<string, object> extraClaims)
    {
        var now = DateTime.UtcNow;
        var claims = new Dictionary<string, object>(extraClaims) { ["sub"] = subject };
        if (!string.IsNullOrWhiteSpace(scope))
        {
            claims["scope"] = scope;
        }

        return TokenHandler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(_lifetime),
            SigningCredentials = _signingCredentials,
        });
    }

    private IResult TokenResponse(string accessToken, string? issuedTokenType) => Results.Json(new
    {
        access_token = accessToken,
        issued_token_type = issuedTokenType,
        token_type = "Bearer",
        expires_in = (int)_lifetime.TotalSeconds,
    });

    private static IResult Error(int status, string error, string description) =>
        Results.Json(new { error, error_description = description }, statusCode: status);

    internal static bool FixedTimeEquals(string actual, string expected) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));
}
