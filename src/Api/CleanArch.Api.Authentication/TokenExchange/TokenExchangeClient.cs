using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace CleanArch.Api.Authentication;

/// <summary>
/// Performs the OAuth2 token exchange (RFC 8693) against an OIDC provider's token endpoint. Sends the
/// caller's token as the <c>subject_token</c> and authenticates this API as a confidential client (HTTP
/// Basic). The provider returns an access token with the SAME subject (the user) but the downstream
/// audience — the heart of the On-Behalf-Of flow. This is the standard grant, so it works against any
/// RFC 8693-compliant provider (Keycloak, Auth0, Duende, Okta custom auth servers, …); point
/// <see cref="OnBehalfOfOptions.TokenEndpoint"/> at the provider's token endpoint. Uses the named
/// <see cref="HttpClientName"/> client so it carries no ambient per-request state and is safe to resolve
/// as a singleton (and from inside a message handler).
/// </summary>
internal sealed partial class TokenExchangeClient : ITokenExchangeClient
{
    public const string HttpClientName = "OnBehalfOf.TokenExchange";

    private const string TokenExchangeGrant = "urn:ietf:params:oauth:grant-type:token-exchange";
    private const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OnBehalfOfOptions _options;
    private readonly ITokenEndpointClientAuthenticator _clientAuthenticator;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TokenExchangeClient> _logger;

    public TokenExchangeClient(
        IHttpClientFactory httpClientFactory,
        IOptions<OnBehalfOfOptions> options,
        ITokenEndpointClientAuthenticator clientAuthenticator,
        TimeProvider timeProvider,
        ILogger<TokenExchangeClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _clientAuthenticator = clientAuthenticator;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ExchangedToken> ExchangeAsync(string subjectToken, CancellationToken cancellationToken)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", TokenExchangeGrant),
            new("subject_token", subjectToken),
            new("subject_token_type", AccessTokenType),
            new("requested_token_type", AccessTokenType),
        };

        // Audience and scope are optional — only send them when configured, so a setup that infers the
        // downstream from the client's grants isn't handed empty values.
        if (!string.IsNullOrWhiteSpace(_options.Audience))
        {
            form.Add(new("audience", _options.Audience));
        }

        if (!string.IsNullOrWhiteSpace(_options.Scope))
        {
            form.Add(new("scope", _options.Scope));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenEndpoint);

        // Prove WHO is asking (this API as a confidential client): client_secret or private_key_jwt,
        // per configuration. The strategy may add an Authorization header and/or form fields, so apply it
        // before the form content is finalised.
        _clientAuthenticator.Apply(request, form);
        request.Content = new FormUrlEncodedContent(form);

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                   && !cancellationToken.IsCancellationRequested)
        {
            // Network failure or the per-attempt timeout fired (not the caller cancelling) — the provider
            // is effectively unavailable. Distinct from a rejection so the host can answer 502, not 401.
            LogProviderUnavailable(ex);
            throw new TokenExchangeException(
                TokenExchangeFailure.ProviderUnavailable, "The token exchange provider was unreachable.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // A 4xx means the provider rejected the SUBJECT TOKEN (e.g. invalid_grant for an
                // expired/foreign token) — the caller's problem (401). A 5xx is the provider's (502).
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                LogExchangeFailed((int)response.StatusCode, error);

                var failure = (int)response.StatusCode >= 500
                    ? TokenExchangeFailure.ProviderUnavailable
                    : TokenExchangeFailure.SubjectRejected;
                throw new TokenExchangeException(
                    failure, $"Token exchange failed with status {(int)response.StatusCode}.");
            }

            var payload = await response.Content.ReadFromJsonAsync<TokenExchangeResponse>(cancellationToken);
            if (payload is null || string.IsNullOrWhiteSpace(payload.AccessToken))
            {
                throw new TokenExchangeException(
                    TokenExchangeFailure.ProviderUnavailable, "Token exchange returned no access token.");
            }

            // Honour the provider's own lifetime so the cache never serves a token past its validity. RFC
            // 8693 makes expires_in only RECOMMENDED, so fall back to a conservative minute when it's absent.
            var lifetime = payload.ExpiresIn > 0 ? payload.ExpiresIn : 60;
            return new ExchangedToken(
                payload.AccessToken, _timeProvider.GetUtcNow().AddSeconds(lifetime));
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "On-Behalf-Of token exchange failed with {StatusCode}: {Error}")]
    private partial void LogExchangeFailed(int statusCode, string error);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "On-Behalf-Of token exchange could not reach the provider")]
    private partial void LogProviderUnavailable(Exception exception);

    private sealed record TokenExchangeResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
