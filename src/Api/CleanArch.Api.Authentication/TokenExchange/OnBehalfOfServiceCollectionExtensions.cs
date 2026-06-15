using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CleanArch.Api.Authentication;

public static class OnBehalfOfServiceCollectionExtensions
{
    /// <summary>
    /// Registers the OAuth2 On-Behalf-Of (token exchange, RFC 8693) pipeline so the API can call a
    /// downstream API AS the authenticated user. Provider-neutral: works against any RFC 8693-compliant
    /// OIDC provider (Keycloak, Auth0, Duende, Okta custom auth servers, …) — set
    /// <c>OnBehalfOf:TokenEndpoint</c> to that provider's token endpoint. Wires the token-exchange client
    /// (behind an expiry-aware, local-only caching decorator, the same inner + decorator shape as the
    /// API-key validator) and the <see cref="OnBehalfOfHandler"/>. The host attaches the handler to
    /// whichever downstream <c>HttpClient</c> it wants to flow the user identity through:
    /// <code>services.AddHttpClient("downstream", c => c.BaseAddress = uri).AddHttpMessageHandler&lt;OnBehalfOfHandler&gt;();</code>
    /// </summary>
    public static IServiceCollection AddOnBehalfOf(
        this IServiceCollection services, IConfiguration configuration)
    {
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
            var opts = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OnBehalfOfOptions>>();
            return opts.Value.ClientAuthentication == ClientAuthenticationMethod.PrivateKeyJwt
                ? new PrivateKeyJwtAuthenticator(opts, provider.GetRequiredService<TimeProvider>())
                : new ClientSecretAuthenticator(opts);
        });

        // Concrete inner + caching decorator (mirrors the API-key validator and the AD user directory).
        services.AddSingleton<TokenExchangeClient>();
        services.AddSingleton<ITokenExchangeClient>(provider => new CachingTokenExchangeClient(
            provider.GetRequiredService<TokenExchangeClient>(),
            provider.GetRequiredService<HybridCache>(),
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OnBehalfOfOptions>>(),
            provider.GetRequiredService<TimeProvider>()));

        // Transient per the IHttpClientFactory handler lifetime; attached to downstream clients by the host.
        // Registered via a factory because the handler's constructor takes the internal ITokenExchangeClient
        // (a public constructor can't expose it), so the default DI activator can't be used.
        services.AddTransient(provider => new OnBehalfOfHandler(
            provider.GetRequiredService<IHttpContextAccessor>(),
            provider.GetRequiredService<ITokenExchangeClient>()));

        return services;
    }
}
