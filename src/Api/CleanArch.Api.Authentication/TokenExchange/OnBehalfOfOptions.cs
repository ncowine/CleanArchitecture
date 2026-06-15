namespace CleanArch.Api.Authentication;

/// <summary>
/// Configuration for the OAuth2 On-Behalf-Of (token exchange, RFC 8693) flow: the API swaps the
/// caller's incoming access token for a NEW token that keeps the same subject (the original user) but
/// is audience-scoped to a downstream API. Bound from the <c>OnBehalfOf</c> section.
/// </summary>
internal sealed class OnBehalfOfOptions
{
    public const string SectionName = "OnBehalfOf";

    /// <summary>
    /// The OIDC provider's token endpoint that performs the exchange (e.g. Keycloak
    /// <c>…/protocol/openid-connect/token</c>, or an Okta custom auth server's <c>…/v1/token</c>). Required
    /// for the flow to be active.
    /// </summary>
    public string? TokenEndpoint { get; set; }

    /// <summary>This API's own client id — the confidential client that is permitted to exchange tokens.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// This API's client secret. Authenticates the exchange request (HTTP Basic at the token endpoint).
    /// SECRET — do NOT commit a real value to appsettings.json. Supply it from a secure source that the
    /// configuration system overlays on top of this section: environment variable
    /// (<c>OnBehalfOf__ClientSecret</c>), a secrets manager such as Azure Key Vault / AWS Secrets Manager,
    /// or user-secrets in local development (<c>dotnet user-secrets set "OnBehalfOf:ClientSecret" ...</c>).
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// How this API authenticates itself to the token endpoint: a shared <c>ClientSecret</c> (default) or
    /// <c>PrivateKeyJwt</c> (sign a JWT assertion with a private key — the secret never leaves the service).
    /// </summary>
    public ClientAuthenticationMethod ClientAuthentication { get; set; } = ClientAuthenticationMethod.ClientSecret;

    /// <summary>
    /// PEM-encoded RSA private key used to sign the client assertion when
    /// <see cref="ClientAuthentication"/> is <see cref="ClientAuthenticationMethod.PrivateKeyJwt"/>.
    /// SECRET — supply from a secure source (env / Key Vault / user-secrets), never committed config.
    /// The matching PUBLIC key is registered with the provider (its JWKS) so it can verify the assertion.
    /// </summary>
    public string? SigningKeyPem { get; set; }

    /// <summary>
    /// The key id (<c>kid</c>) stamped on the assertion header, so the provider knows which registered
    /// public key to verify it with. Required when the provider publishes more than one key.
    /// </summary>
    public string? SigningKeyId { get; set; }

    /// <summary>
    /// The downstream API the exchanged token is for — becomes the new token's <c>aud</c>. Optional;
    /// some IdP setups infer the audience from the requested scopes instead.
    /// </summary>
    public string? Audience { get; set; }

    /// <summary>Space-delimited scopes to request for the downstream call. Optional.</summary>
    public string? Scope { get; set; }

    /// <summary>
    /// How long before a cached downstream token's real expiry to stop serving it and re-exchange. Guards
    /// against a token that is valid when fetched from cache but expires in flight to the downstream.
    /// </summary>
    public int ExpirySkewSeconds { get; set; } = 60;

    /// <summary>Per-attempt timeout for the call to the provider's token endpoint.</summary>
    public int TokenEndpointTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// True once a token endpoint, client id, and the credential for the chosen authentication method are
    /// present — otherwise OBO is off.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TokenEndpoint)
        && !string.IsNullOrWhiteSpace(ClientId)
        && (ClientAuthentication == ClientAuthenticationMethod.PrivateKeyJwt
            ? !string.IsNullOrWhiteSpace(SigningKeyPem)
            : !string.IsNullOrWhiteSpace(ClientSecret));
}
