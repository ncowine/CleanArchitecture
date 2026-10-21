using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace CleanArch.Api.Authentication;

/// <summary>
/// Outbound <see cref="DelegatingHandler"/> that passes the caller's OWN access token through to a
/// downstream, unchanged, as <c>Authorization: Bearer</c>. No exchange, no IdP round-trip.
/// <para>
/// Only for a downstream inside the same trust boundary that is deliberately configured to accept THIS
/// API's audience — the forwarded token still says <c>aud = this API</c>, and a downstream that validates
/// its audience properly will reject it. Anything else should use On-Behalf-Of, which mints a token for
/// the downstream's own audience. The token is forwarded with whatever lifetime it has left; there is no
/// refresh.
/// </para>
/// <code>
/// services.AddHttpClient("reports", c => c.BaseAddress = reportsUri).AddForwardedBearer();
/// </code>
/// A caller who authenticated by API key or Basic has no bearer token to forward: the call fails with
/// <see cref="CallerTokenRequiredException"/> (a 401) — it never falls back to a service identity.
/// </summary>
public sealed partial class ForwardedBearerHandler : DelegatingHandler
{
    private readonly string _downstreamName;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ForwardedBearerHandler> _logger;

    internal ForwardedBearerHandler(
        string downstreamName, IHttpContextAccessor httpContextAccessor, ILogger<ForwardedBearerHandler> logger)
    {
        _downstreamName = downstreamName;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                $"Forwarding the caller's token to downstream '{_downstreamName}' requires an active HTTP " +
                "request. Background work has no caller — give it a client without this handler.");

        // The validated token the JWT handler kept on the request (SaveToken = true). Read from the bearer
        // scheme explicitly, so an API-key or Basic caller yields nothing rather than some other credential.
        var callerToken = await httpContext.GetTokenAsync(
            JwtBearerDefaults.AuthenticationScheme, "access_token");
        if (string.IsNullOrWhiteSpace(callerToken))
        {
            throw new CallerTokenRequiredException(
                _downstreamName,
                $"Downstream '{_downstreamName}' is called with the caller's own bearer token, but this " +
                "request was not authenticated with one.");
        }

        LogForwarded(
            _downstreamName, httpContext.User.Identity?.Name ?? "(unnamed)",
            request.Method.Method, request.RequestUri?.AbsolutePath);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", callerToken);

        return await base.SendAsync(request, cancellationToken);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "Forwarded the caller's bearer token to downstream '{Downstream}' as user '{User}' " +
                  "on {Method} {Path}")]
    private partial void LogForwarded(string downstream, string user, string method, string? path);
}

/// <summary>
/// A downstream needs the caller's own bearer token, and the caller did not authenticate with one (API key
/// or Basic). Public so the API host can answer 401 — the caller must sign in as a user to use this route.
/// </summary>
public sealed class CallerTokenRequiredException : Exception
{
    public CallerTokenRequiredException(string downstreamName, string message)
        : base(message) => DownstreamName = downstreamName;

    /// <summary>Which downstream needed the token.</summary>
    public string DownstreamName { get; }
}
