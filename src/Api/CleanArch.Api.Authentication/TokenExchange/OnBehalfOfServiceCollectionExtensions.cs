using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CleanArch.Api.Authentication;

public static class OnBehalfOfServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared OAuth2 On-Behalf-Of (token exchange, RFC 8693) pipeline so the API can call
    /// downstream APIs AS the authenticated user. Provider-neutral: works against any RFC 8693-compliant
    /// OIDC provider (Okta custom auth servers, Keycloak, Auth0, Duende, …) — set
    /// <c>OnBehalfOf:TokenEndpoint</c> to that provider's token endpoint.
    /// <para>
    /// This registers the machinery ONCE. Each downstream is then declared where its <c>HttpClient</c> is,
    /// with <see cref="AddOnBehalfOf(IHttpClientBuilder, string?)"/>:
    /// </para>
    /// <code>
    /// services.AddOnBehalfOf(configuration);
    /// services.AddHttpClient("billing", c => c.BaseAddress = billingUri).AddOnBehalfOf();
    /// services.AddHttpClient("grading", c => c.BaseAddress = gradingUri).AddOnBehalfOf();
    /// </code>
    /// </summary>
    public static IServiceCollection AddOnBehalfOf(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Holds the configuration the named downstream sections bind from, and the names registered so
        // far. A single instance shared by every AddOnBehalfOf call on this collection.
        if (FindRegistry(services) is null)
        {
            services.AddSingleton(new OnBehalfOfRegistry(configuration));
        }

        services.AddOptions<OnBehalfOfOptions>()
            .Bind(configuration.GetSection(OnBehalfOfOptions.SectionName));

        // The cache decorator and exchange client stamp/check token expiry against this clock (swappable
        // in tests). TryAdd so a host that already registered one wins.
        services.TryAddSingleton(TimeProvider.System);

        // Dedicated client for the IdP token endpoint — keeps the exchange's HTTP concerns off the
        // downstream client and lets the exchange client stay a singleton (resolved from a message handler).
        // The token endpoint is now a per-request dependency, so it gets resilience: retries on transient
        // 5xx/timeouts, a per-attempt timeout, and a circuit breaker that sheds load when it's down.
        var attemptTimeout = TimeSpan.FromSeconds(
            configuration.GetValue($"{OnBehalfOfOptions.SectionName}:TokenEndpointTimeoutSeconds", 10));
        services.AddHttpClient(TokenExchangeClient.HttpClientName)
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = attemptTimeout;
                options.TotalRequestTimeout.Timeout = attemptTimeout * 3;
                options.CircuitBreaker.SamplingDuration = attemptTimeout * 2;
            });

        // How this API proves its own identity to the token endpoint: a shared secret, or a private-key
        // JWT assertion (no secret on the wire). Selected by OnBehalfOf:ClientAuthentication.
        services.AddSingleton<ITokenEndpointClientAuthenticator>(provider =>
        {
            var opts = provider.GetRequiredService<IOptions<OnBehalfOfOptions>>();
            return opts.Value.ClientAuthentication == ClientAuthenticationMethod.PrivateKeyJwt
                ? new PrivateKeyJwtAuthenticator(opts, provider.GetRequiredService<TimeProvider>())
                : new ClientSecretAuthenticator(opts);
        });

        // Concrete inner + caching decorator (mirrors the API-key validator and the AD user directory).
        services.AddSingleton<TokenExchangeClient>();
        services.AddSingleton<ITokenExchangeClient>(provider => new CachingTokenExchangeClient(
            provider.GetRequiredService<TokenExchangeClient>(),
            provider.GetRequiredService<HybridCache>(),
            provider.GetRequiredService<IOptions<OnBehalfOfOptions>>(),
            provider.GetRequiredService<TimeProvider>()));

        return services;
    }

    /// <summary>
    /// Flows the authenticated user's identity through this <c>HttpClient</c>, by exchanging their token
    /// for one scoped to this downstream before every outbound call. Attach it to each downstream that
    /// does its own per-user authorization:
    /// <code>
    /// services.AddHttpClient("billing", c => c.BaseAddress = billingUri).AddOnBehalfOf();
    /// </code>
    /// The downstream's audience and scope are bound from <c>OnBehalfOf:Downstreams:{name}</c>, where
    /// <c>{name}</c> defaults to the <c>HttpClient</c>'s own name — so the client and its configuration
    /// section cannot drift apart. A missing or empty section fails at STARTUP, naming the downstream,
    /// rather than at the first call as a puzzling 401 from the far end.
    /// <para>
    /// Only for downstreams that need the USER. A service-to-service call that just needs to know your
    /// API asked should use a plain client with client-credentials — an exchange there costs an IdP
    /// round-trip and buys no authorization.
    /// </para>
    /// </summary>
    /// <param name="builder">The downstream's HTTP client builder.</param>
    /// <param name="downstreamName">
    /// Configuration key under <c>OnBehalfOf:Downstreams</c>. Defaults to the client's name; pass one only
    /// when two clients must share a downstream's audience, or the client's name isn't a good config key.
    /// </param>
    public static IHttpClientBuilder AddOnBehalfOf(
        this IHttpClientBuilder builder, string? downstreamName = null)
    {
        var name = downstreamName ?? builder.Name;

        var registry = FindRegistry(builder.Services)
            ?? throw new InvalidOperationException(
                $"AddOnBehalfOf() on HTTP client '{builder.Name}' requires the On-Behalf-Of services to be " +
                "registered first. Call services.AddOnBehalfOf(configuration) during startup.");

        if (!registry.Names.Contains(name, StringComparer.Ordinal))
        {
            registry.Names.Add(name);
        }

        // Named options, one per downstream. Validated at startup so a typo'd section is a boot failure
        // that names the downstream, not a runtime 401 from a service that was sent the wrong audience.
        builder.Services.AddOptions<DownstreamTokenOptions>(name)
            .Bind(registry.Configuration.GetSection(DownstreamTokenOptions.SectionFor(name)))
            .Validate(
                options => options.IsUsable,
                $"'{DownstreamTokenOptions.SectionFor(name)}' must set Audience and/or Scope. Downstream " +
                $"'{name}' is registered for On-Behalf-Of but has no audience to mint tokens for — the " +
                "section is missing, misspelled, or empty in this environment.")
            .ValidateOnStart();

        // The root credentials are only required once something actually uses them, so the check is
        // registered here rather than in AddOnBehalfOf — an application with no OBO downstreams should
        // not be forced to configure a token endpoint. TryAddEnumerable keeps it to one validator.
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<OnBehalfOfOptions>, OnBehalfOfOptionsValidator>());
        builder.Services.AddOptions<OnBehalfOfOptions>().ValidateOnStart();

        // Logs what was wired, at startup, once. TryAddEnumerable dedupes by implementation type.
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, OnBehalfOfStartupLogger>());

        // Built by hand rather than by the DI activator: the handler is bound to ONE downstream name and
        // takes the internal exchange client, neither of which the default activator can supply.
        return builder.AddHttpMessageHandler(provider => new OnBehalfOfHandler(
            name,
            provider.GetRequiredService<IHttpContextAccessor>(),
            provider.GetRequiredService<ITokenExchangeClient>(),
            provider.GetRequiredService<IOptionsMonitor<DownstreamTokenOptions>>(),
            provider.GetRequiredService<ILogger<OnBehalfOfHandler>>()));
    }

    /// <summary>
    /// The registry instance already on the collection, if <c>AddOnBehalfOf(configuration)</c> ran. Read
    /// off the descriptor rather than a built provider because both extensions run at registration time.
    /// </summary>
    private static OnBehalfOfRegistry? FindRegistry(IServiceCollection services) =>
        services.FirstOrDefault(descriptor => descriptor.ServiceType == typeof(OnBehalfOfRegistry))
            ?.ImplementationInstance as OnBehalfOfRegistry;
}

/// <summary>
/// Fails startup when a downstream is registered for On-Behalf-Of but this API's own credentials for the
/// token endpoint are missing — the failure would otherwise surface as a 401 or 502 on the first real
/// user request, in whichever environment forgot to set the secret.
/// </summary>
internal sealed class OnBehalfOfOptionsValidator : IValidateOptions<OnBehalfOfOptions>
{
    public ValidateOptionsResult Validate(string? name, OnBehalfOfOptions options) =>
        options.IsConfigured
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "On-Behalf-Of downstreams are registered, but the token exchange is not configured: " +
                $"{options.DescribeWhatIsMissing()}.");
}

/// <summary>
/// Writes one startup line per configured downstream. With several downstreams and several environments,
/// "which audience is this deployment actually going to ask for?" is the question you need answered
/// before anything else, and inferring it from scattered environment variables is how afternoons vanish.
/// </summary>
internal sealed partial class OnBehalfOfStartupLogger : IHostedService
{
    private readonly OnBehalfOfRegistry _registry;
    private readonly IOptionsMonitor<DownstreamTokenOptions> _downstreamOptions;
    private readonly OnBehalfOfOptions _options;
    private readonly ILogger<OnBehalfOfStartupLogger> _logger;

    public OnBehalfOfStartupLogger(
        OnBehalfOfRegistry registry,
        IOptionsMonitor<DownstreamTokenOptions> downstreamOptions,
        IOptions<OnBehalfOfOptions> options,
        ILogger<OnBehalfOfStartupLogger> logger)
    {
        _registry = registry;
        _downstreamOptions = downstreamOptions;
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        LogConfigured(
            _options.TokenEndpoint, _options.ClientId, _options.ClientAuthentication,
            _registry.Names.Count);

        foreach (var name in _registry.Names)
        {
            var downstream = _downstreamOptions.Get(name);
            LogDownstream(name, downstream.Audience, downstream.Scope);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "On-Behalf-Of ready: token endpoint {TokenEndpoint}, client {ClientId} authenticating " +
                  "by {ClientAuthentication}, {DownstreamCount} downstream(s)")]
    private partial void LogConfigured(
        string? tokenEndpoint,
        string clientId,
        ClientAuthenticationMethod clientAuthentication,
        int downstreamCount);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "On-Behalf-Of downstream '{Downstream}' will request audience {Audience} scope {Scope}")]
    private partial void LogDownstream(string downstream, string? audience, string? scope);
}
