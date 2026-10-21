using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

namespace CleanArch.Api.Authentication;

/// <summary>
/// Outbound <see cref="DelegatingHandler"/> that authenticates to ONE downstream with this API's service
/// account over HTTP Basic, from <c>DownstreamCredentials:{name}:Username/Password</c>. For legacy
/// downstreams that take nothing better. Attached through
/// <see cref="DownstreamAuthenticationExtensions.AddBasicCredentials(IHttpClientBuilder, string?)"/>.
/// </summary>
public sealed partial class BasicCredentialsHandler : DelegatingHandler
{
    private readonly string _downstreamName;
    private readonly IOptionsMonitor<DownstreamCredentialOptions> _options;
    private readonly ILogger<BasicCredentialsHandler> _logger;

    internal BasicCredentialsHandler(
        string downstreamName,
        IOptionsMonitor<DownstreamCredentialOptions> options,
        ILogger<BasicCredentialsHandler> logger)
    {
        _downstreamName = downstreamName;
        _options = options;
        _logger = logger;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Read per call so a rotated password is picked up on configuration reload, without a restart.
        var options = _options.Get(_downstreamName);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        LogAttached(_downstreamName, options.Username, request.Method.Method, request.RequestUri?.AbsolutePath);
        return base.SendAsync(request, cancellationToken);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "Attached Basic credentials for downstream '{Downstream}' as '{Username}' on {Method} {Path}")]
    private partial void LogAttached(string downstream, string? username, string method, string? path);
}

/// <summary>
/// Outbound <see cref="DelegatingHandler"/> that authenticates to ONE downstream with this API's own API
/// key, sent in <c>DownstreamCredentials:{name}:ApiKeyHeader</c> (default <c>X-Api-Key</c>). Attached
/// through <see cref="DownstreamAuthenticationExtensions.AddApiKeyCredentials(IHttpClientBuilder, string?)"/>.
/// </summary>
public sealed partial class ApiKeyCredentialsHandler : DelegatingHandler
{
    private readonly string _downstreamName;
    private readonly IOptionsMonitor<DownstreamCredentialOptions> _options;
    private readonly ILogger<ApiKeyCredentialsHandler> _logger;

    internal ApiKeyCredentialsHandler(
        string downstreamName,
        IOptionsMonitor<DownstreamCredentialOptions> options,
        ILogger<ApiKeyCredentialsHandler> logger)
    {
        _downstreamName = downstreamName;
        _options = options;
        _logger = logger;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var options = _options.Get(_downstreamName);

        // Remove first: a retry re-sends the same request message, and the header must not be doubled.
        request.Headers.Remove(options.ApiKeyHeader);
        request.Headers.Add(options.ApiKeyHeader, options.ApiKey);

        LogAttached(_downstreamName, options.ApiKeyHeader, request.Method.Method, request.RequestUri?.AbsolutePath);
        return base.SendAsync(request, cancellationToken);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "Attached an API key for downstream '{Downstream}' in header {Header} on {Method} {Path}")]
    private partial void LogAttached(string downstream, string header, string method, string? path);
}
