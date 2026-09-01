using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace CleanArch.Api.Authentication;

/// <summary>
/// Performs the OAuth2 token exchange (RFC 8693) against an OIDC provider's token endpoint. Sends the
/// caller's token as the <c>subject_token</c> and authenticates this API as a confidential client. The
/// provider returns an access token with the SAME subject (the user) but the audience of the requested
/// downstream — the heart of the On-Behalf-Of flow. This is the standard grant, so it works against any
/// RFC 8693-compliant provider (Okta custom auth servers, Keycloak, Auth0, Duende, …); point
/// <see cref="OnBehalfOfOptions.TokenEndpoint"/> at the provider's token endpoint.
/// <para>
/// The audience and scope come from the <see cref="DownstreamTokenRequest"/> passed per call, NOT from
/// configuration read once — one API typically calls several downstreams, and each needs a token minted
/// for its own audience.
/// </para>
/// <para>
/// Uses the named <see cref="HttpClientName"/> client so it carries no ambient per-request state and is
/// safe to resolve as a singleton (and from inside a message handler). Every call here is a real network
/// round-trip: a log line or a span from this class means a token was actually exchanged, so its absence
/// on a downstream call means the cache served it.
/// </para>
/// </summary>
internal sealed partial class TokenExchangeClient : ITokenExchangeClient
{
    public const string HttpClientName = "OnBehalfOf.TokenExchange";

    private const string TokenExchangeGrant = "urn:ietf:params:oauth:grant-type:token-exchange";
    private const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";

    /// <summary>Upper bound on how much of a provider error body is logged, so an HTML error page can't flood the log.</summary>
    private const int MaxLoggedErrorLength = 512;

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

    public async Task<ExchangedToken> ExchangeAsync(
        string subjectToken, DownstreamTokenRequest downstream, CancellationToken cancellationToken)
    {
        // A span per exchange, so the IdP round-trip shows up as its own segment inside whatever request
        // triggered it instead of unexplained latency on the downstream call.
        using var activity = OnBehalfOfDiagnostics.ActivitySource.StartActivity(
            OnBehalfOfDiagnostics.ExchangeActivityName, ActivityKind.Client);
        activity?.SetTag(OnBehalfOfDiagnostics.Tags.Downstream, downstream.Name);
        activity?.SetTag(OnBehalfOfDiagnostics.Tags.Audience, downstream.Audience);
        activity?.SetTag(OnBehalfOfDiagnostics.Tags.Scope, downstream.Scope);

        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", TokenExchangeGrant),
            new("subject_token", subjectToken),
            new("subject_token_type", AccessTokenType),
            new("requested_token_type", AccessTokenType),
        };

        // Audience and scope are optional — only send them when configured, so a setup that infers the
        // downstream from the client's grants isn't handed empty values.
        if (!string.IsNullOrWhiteSpace(downstream.Audience))
        {
            form.Add(new("audience", downstream.Audience));
        }

        if (!string.IsNullOrWhiteSpace(downstream.Scope))
        {
            form.Add(new("scope", downstream.Scope));
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
            LogProviderUnavailable(downstream.Name, _options.TokenEndpoint, ex);
            throw Fail(activity, TokenExchangeFailure.ProviderUnavailable, downstream,
                $"The token exchange provider was unreachable while getting a token for '{downstream.Name}'.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // A 4xx means the provider rejected the SUBJECT TOKEN or this client's request (e.g.
                // invalid_grant for an expired token, invalid_target for an audience this client is not
                // granted) — the caller's or the config's problem (401). A 5xx is the provider's (502).
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var statusCode = (int)response.StatusCode;
                LogExchangeFailed(downstream.Name, downstream.Audience, statusCode, DescribeError(body));
                activity?.SetTag(OnBehalfOfDiagnostics.Tags.StatusCode, statusCode);

                var failure = statusCode >= 500
                    ? TokenExchangeFailure.ProviderUnavailable
                    : TokenExchangeFailure.SubjectRejected;
                throw Fail(activity, failure, downstream,
                    $"Token exchange for downstream '{downstream.Name}' failed with status {statusCode}.");
            }

            var payload = await response.Content.ReadFromJsonAsync<TokenExchangeResponse>(cancellationToken);
            if (payload is null || string.IsNullOrWhiteSpace(payload.AccessToken))
            {
                throw Fail(activity, TokenExchangeFailure.ProviderUnavailable, downstream,
                    $"Token exchange for downstream '{downstream.Name}' returned no access token.");
            }

            // Honour the provider's own lifetime so the cache never serves a token past its validity. RFC
            // 8693 makes expires_in only RECOMMENDED, so fall back to a conservative minute when it's absent.
            var lifetime = payload.ExpiresIn > 0 ? payload.ExpiresIn : 60;
            LogExchanged(downstream.Name, downstream.Audience, lifetime);
            return new ExchangedToken(
                payload.AccessToken, _timeProvider.GetUtcNow().AddSeconds(lifetime));
        }
    }

    /// <summary>Marks the span failed and builds the exception, so both always carry the same reason.</summary>
    private static TokenExchangeException Fail(
        Activity? activity,
        TokenExchangeFailure failure,
        DownstreamTokenRequest downstream,
        string message,
        Exception? innerException = null)
    {
        activity?.SetTag(OnBehalfOfDiagnostics.Tags.Failure, failure.ToString());
        activity?.SetStatus(ActivityStatusCode.Error, message);
        return new TokenExchangeException(failure, downstream.Name, message, innerException);
    }

    /// <summary>
    /// Turns a provider error body into one readable line. OAuth2 errors are JSON with <c>error</c> and
    /// <c>error_description</c> — the two fields that actually say what went wrong (<c>invalid_grant</c>,
    /// <c>invalid_target</c>, …) — so lift those out and fall back to a truncated body for anything else.
    /// </summary>
    private static string DescribeError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "(empty response body)";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                var error = document.RootElement.TryGetProperty("error", out var e) ? e.ToString() : null;
                var description = document.RootElement.TryGetProperty("error_description", out var d)
                    ? d.ToString()
                    : null;

                if (!string.IsNullOrWhiteSpace(error))
                {
                    return string.IsNullOrWhiteSpace(description) ? error : $"{error}: {description}";
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON (a gateway's HTML error page, say) — fall through to the raw body.
        }

        return body.Length <= MaxLoggedErrorLength ? body : body[..MaxLoggedErrorLength] + "…";
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "On-Behalf-Of exchange for downstream '{Downstream}' (audience {Audience}) was rejected " +
                  "with {StatusCode}: {Error}")]
    private partial void LogExchangeFailed(string downstream, string? audience, int statusCode, string error);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "On-Behalf-Of exchange for downstream '{Downstream}' could not reach the token endpoint {TokenEndpoint}")]
    private partial void LogProviderUnavailable(string downstream, string? tokenEndpoint, Exception exception);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Debug,
        Message = "On-Behalf-Of exchanged a token for downstream '{Downstream}' (audience {Audience}), " +
                  "valid {LifetimeSeconds}s — this was a cache miss")]
    private partial void LogExchanged(string downstream, string? audience, int lifetimeSeconds);

    private sealed record TokenExchangeResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
