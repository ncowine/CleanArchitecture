using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace CleanArch.Api.Authentication;

/// <summary>
/// Outbound <see cref="DelegatingHandler"/> that makes calls to ONE downstream act AS the authenticated
/// user, not as this service. It lifts the caller's incoming access token off the current request,
/// exchanges it for a token scoped to that downstream (OAuth2 On-Behalf-Of / RFC 8693), and attaches the
/// result as the outbound <c>Authorization: Bearer</c>.
/// <para>
/// One instance is bound to one downstream name, which is why it is attached through
/// <see cref="OnBehalfOfServiceCollectionExtensions.AddOnBehalfOf(IHttpClientBuilder, string?)"/> rather
/// than resolved from the container: several downstreams each need a token for their OWN audience, and a
/// single shared handler could only ever mint one.
/// </para>
/// <code>
/// services.AddHttpClient("billing", c => c.BaseAddress = billingUri).AddOnBehalfOf();
/// </code>
/// Requires the incoming caller to have authenticated with an OIDC bearer token (the user's token). A
/// caller that authenticated by API key has no user token to exchange and will fail loudly — service
/// callers should use a separate, non-OBO client.
/// </summary>
public sealed partial class OnBehalfOfHandler : DelegatingHandler
{
    private readonly string _downstreamName;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITokenExchangeClient _tokenExchange;
    private readonly IOptionsMonitor<DownstreamTokenOptions> _downstreamOptions;
    private readonly ILogger<OnBehalfOfHandler> _logger;

    internal OnBehalfOfHandler(
        string downstreamName,
        IHttpContextAccessor httpContextAccessor,
        ITokenExchangeClient tokenExchange,
        IOptionsMonitor<DownstreamTokenOptions> downstreamOptions,
        ILogger<OnBehalfOfHandler> logger)
    {
        _downstreamName = downstreamName;
        _httpContextAccessor = httpContextAccessor;
        _tokenExchange = tokenExchange;
        _downstreamOptions = downstreamOptions;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                $"On-Behalf-Of for downstream '{_downstreamName}' requires an active HTTP request; " +
                "there is no caller token to exchange. Background work has no user to act as — give it a " +
                "client without this handler.");

        // The caller's validated token, stashed by the JWT handler (SaveTokens = true). Absent when the
        // caller authenticated some other way (API key / Basic) — OBO is impossible, so fail clearly.
        var subjectToken = await httpContext.GetTokenAsync(
            JwtBearerDefaults.AuthenticationScheme, "access_token");
        if (string.IsNullOrWhiteSpace(subjectToken))
        {
            // No user token to act on behalf of (e.g. an API-key/Basic caller). Surfaced as a rejected
            // subject so the host answers 401 — not a 500 — and never falls back to a service identity.
            throw new TokenExchangeException(
                TokenExchangeFailure.SubjectRejected,
                _downstreamName,
                $"On-Behalf-Of for downstream '{_downstreamName}' requires the caller to be authenticated " +
                "with an OIDC bearer token; no incoming access token was found to exchange.");
        }

        // Read through the monitor on every call rather than caching the values, so a configuration
        // reload re-points this downstream without a restart (the cache key includes them, so a stale
        // token can never be served for the new audience).
        var options = _downstreamOptions.Get(_downstreamName);
        var downstream = new DownstreamTokenRequest(_downstreamName, options.Audience, options.Scope);

        var downstreamToken = await _tokenExchange.ExchangeAsync(subjectToken, downstream, cancellationToken);

        LogAttached(
            _downstreamName, options.Audience, httpContext.User.Identity?.Name ?? "(unnamed)",
            request.Method.Method, request.RequestUri?.AbsolutePath);

        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", downstreamToken.AccessToken);

        return await base.SendAsync(request, cancellationToken);
    }

    // Debug rather than Information: one line per outbound call is too much for normal running, but it is
    // exactly what you turn on to answer "which audience did we actually send to that service, and as
    // whom?". Pair it with the exchange log — an attach with no exchange means the cache served the token.
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "On-Behalf-Of attached a token for downstream '{Downstream}' (audience {Audience}) " +
                  "as user '{User}' on {Method} {Path}")]
    private partial void LogAttached(
        string downstream, string? audience, string user, string method, string? path);
}
