using Microsoft.Extensions.Options;

namespace CleanArch.Api.Authentication;

/// <summary>
/// Outbound <see cref="DelegatingHandler"/> that makes calls to ONE downstream act as THIS SERVICE: it gets
/// a <c>client_credentials</c> token audience-scoped to that downstream (cached until near expiry) and
/// attaches it as the outbound <c>Authorization: Bearer</c>.
/// <para>
/// The counterpart of <see cref="OnBehalfOfHandler"/> for downstreams that do not authorize per user. It
/// never reads the incoming request, so it works the same from a user's request, an API-key caller, or
/// background work. Attached through
/// <see cref="OnBehalfOfServiceCollectionExtensions.AddClientCredentials(IHttpClientBuilder, string?)"/>.
/// </para>
/// </summary>
public sealed partial class ClientCredentialsHandler : DelegatingHandler
{
    private readonly string _downstreamName;
    private readonly ITokenExchangeClient _tokenClient;
    private readonly IOptionsMonitor<DownstreamTokenOptions> _downstreamOptions;
    private readonly ILogger<ClientCredentialsHandler> _logger;

    internal ClientCredentialsHandler(
        string downstreamName,
        ITokenExchangeClient tokenClient,
        IOptionsMonitor<DownstreamTokenOptions> downstreamOptions,
        ILogger<ClientCredentialsHandler> logger)
    {
        _downstreamName = downstreamName;
        _tokenClient = tokenClient;
        _downstreamOptions = downstreamOptions;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Read per call, like the OBO handler, so a configuration reload re-points this downstream.
        var options = _downstreamOptions.Get(_downstreamName);
        var downstream = new DownstreamTokenRequest(_downstreamName, options.Audience, options.Scope);

        var token = await _tokenClient.ClientCredentialsAsync(downstream, cancellationToken);

        LogAttached(_downstreamName, options.Audience, request.Method.Method, request.RequestUri?.AbsolutePath);

        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", token.AccessToken);

        return await base.SendAsync(request, cancellationToken);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "Client credentials attached a service token for downstream '{Downstream}' " +
                  "(audience {Audience}) on {Method} {Path}")]
    private partial void LogAttached(string downstream, string? audience, string method, string? path);
}
