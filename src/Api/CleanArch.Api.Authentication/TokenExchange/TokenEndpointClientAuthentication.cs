using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CleanArch.Api.Authentication;

/// <summary>How this API proves its own identity to the provider's token endpoint.</summary>
public enum ClientAuthenticationMethod
{
    /// <summary>Send <c>client_id</c> + <c>client_secret</c> (HTTP Basic). Simple; the secret is shared.</summary>
    ClientSecret,

    /// <summary>
    /// Sign a short-lived JWT assertion with a private key (RFC 7523 <c>private_key_jwt</c>). The provider
    /// verifies it with the registered public key, so the private key never leaves this service.
    /// </summary>
    PrivateKeyJwt,
}

/// <summary>
/// Strategy for authenticating THIS API as a confidential client on the token-exchange request — the
/// "who is asking?" half of the call (the other half is the user's subject token). Either approach is
/// applied to the outbound request just before its form content is built.
/// </summary>
internal interface ITokenEndpointClientAuthenticator
{
    void Apply(HttpRequestMessage request, IList<KeyValuePair<string, string>> form);
}

/// <summary>client_secret_basic: the client id and secret as an HTTP Basic header.</summary>
internal sealed class ClientSecretAuthenticator : ITokenEndpointClientAuthenticator
{
    private readonly string _credentials;

    public ClientSecretAuthenticator(IOptions<OnBehalfOfOptions> options)
    {
        var o = options.Value;
        _credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{o.ClientId}:{o.ClientSecret}"));
    }

    public void Apply(HttpRequestMessage request, IList<KeyValuePair<string, string>> form) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _credentials);
}

/// <summary>
/// private_key_jwt: mints a fresh, short-lived JWT assertion signed with the configured private key and
/// sends it as <c>client_assertion</c>. The assertion's <c>iss</c>/<c>sub</c> are the client id and its
/// <c>aud</c> is the token endpoint, per RFC 7523 — so it cannot be replayed against a different endpoint.
/// A unique <c>jti</c> and a 2-minute lifetime bound replay further. No secret ever crosses the wire.
/// </summary>
internal sealed class PrivateKeyJwtAuthenticator : ITokenEndpointClientAuthenticator
{
    private const string AssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";
    private static readonly JsonWebTokenHandler TokenHandler = new();

    private readonly OnBehalfOfOptions _options;
    private readonly SigningCredentials _signingCredentials;
    private readonly TimeProvider _timeProvider;

    public PrivateKeyJwtAuthenticator(IOptions<OnBehalfOfOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;

        // Load the private key once and reuse the signing credentials (IdentityModel is built for
        // concurrent token creation). The PEM is a secret — it must come from a secure config source.
        var rsa = RSA.Create();
        rsa.ImportFromPem(_options.SigningKeyPem);
        var key = new RsaSecurityKey(rsa) { KeyId = _options.SigningKeyId };
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
    }

    public void Apply(HttpRequestMessage request, IList<KeyValuePair<string, string>> form)
    {
        var now = _timeProvider.GetUtcNow();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.ClientId,
            Audience = _options.TokenEndpoint,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = _options.ClientId,
                ["jti"] = Guid.NewGuid().ToString("N"),
            },
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.AddMinutes(2).UtcDateTime,
            SigningCredentials = _signingCredentials,
        };

        var assertion = TokenHandler.CreateToken(descriptor);

        form.Add(new("client_id", _options.ClientId));
        form.Add(new("client_assertion_type", AssertionType));
        form.Add(new("client_assertion", assertion));
    }
}
