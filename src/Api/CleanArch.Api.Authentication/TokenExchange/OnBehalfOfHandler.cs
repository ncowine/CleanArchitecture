using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace CleanArch.Api.Authentication;

/// <summary>
/// Outbound <see cref="DelegatingHandler"/> that makes a downstream call act AS the authenticated user,
/// not as this service. It lifts the caller's incoming access token off the current request, exchanges it
/// for a downstream-scoped token (OAuth2 On-Behalf-Of / RFC 8693), and attaches that token as the
/// outbound <c>Authorization: Bearer</c>. Attach it to any <c>HttpClient</c> this API uses to reach a
/// downstream API:
/// <code>services.AddHttpClient("downstream", c => c.BaseAddress = ...).AddHttpMessageHandler&lt;OnBehalfOfHandler&gt;();</code>
/// Requires the incoming caller to have authenticated with an OIDC bearer token (the user's token). A
/// caller that authenticated by API key has no user token to exchange and will fail loudly — service
/// callers should use a separate, non-OBO client.
/// </summary>
public sealed class OnBehalfOfHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITokenExchangeClient _tokenExchange;

    internal OnBehalfOfHandler(IHttpContextAccessor httpContextAccessor, ITokenExchangeClient tokenExchange)
    {
        _httpContextAccessor = httpContextAccessor;
        _tokenExchange = tokenExchange;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                "On-Behalf-Of requires an active HTTP request; there is no caller token to exchange.");

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
                "On-Behalf-Of requires the caller to be authenticated with an OIDC bearer token; " +
                "no incoming access token was found to exchange.");
        }

        var downstreamToken = await _tokenExchange.ExchangeAsync(subjectToken, cancellationToken);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", downstreamToken.AccessToken);

        return await base.SendAsync(request, cancellationToken);
    }
}
