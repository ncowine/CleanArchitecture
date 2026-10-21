using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CleanArch.Api.Authentication;

/// <summary>
/// How a downstream <c>HttpClient</c> authenticates. Each downstream gets exactly ONE, chosen where its
/// client is registered — what a downstream accepts is a fact about that service, not something to
/// negotiate per call. Pick by asking "does the downstream need to know WHICH USER is calling?":
/// <list type="table">
/// <item><term>The user, and it accepts this API's audience</term><description><see cref="AddForwardedBearer"/></description></item>
/// <item><term>The user, with its own audience</term><description><c>AddOnBehalfOf()</c> (token exchange)</description></item>
/// <item><term>Only that this API is calling</term><description><c>AddClientCredentials()</c></description></item>
/// <item><term>It only takes legacy credentials</term><description><see cref="AddBasicCredentials"/> / <see cref="AddApiKeyCredentials"/></description></item>
/// </list>
/// A downstream that accepts several still gets one — pick from the table. Never chain them ("on a 401,
/// retry with Basic"): that quietly turns a call made as the user into a call made as the service.
/// See <c>docs/downstream-authentication.md</c>.
/// </summary>
public static class DownstreamAuthenticationExtensions
{
    /// <summary>
    /// Passes the caller's own bearer token through to this downstream, unchanged. Only for a downstream
    /// in the same trust boundary that accepts THIS API's audience — otherwise use <c>AddOnBehalfOf()</c>.
    /// Needs no configuration. A caller without a bearer token (API key / Basic) gets a 401.
    /// <code>
    /// services.AddHttpClient("reports", c => c.BaseAddress = reportsUri).AddForwardedBearer();
    /// </code>
    /// </summary>
    public static IHttpClientBuilder AddForwardedBearer(this IHttpClientBuilder builder)
    {
        builder.Services.AddHttpContextAccessor();

        var name = builder.Name;
        return builder.AddHttpMessageHandler(provider => new ForwardedBearerHandler(
            name,
            provider.GetRequiredService<IHttpContextAccessor>(),
            provider.GetRequiredService<ILogger<ForwardedBearerHandler>>()));
    }

    /// <summary>
    /// Authenticates to this downstream with this API's service account over HTTP Basic, from
    /// <c>DownstreamCredentials:{name}:Username</c> and <c>:Password</c>. Missing values fail at startup.
    /// <code>
    /// services.AddHttpClient("legacy", c => c.BaseAddress = legacyUri).AddBasicCredentials();
    /// </code>
    /// </summary>
    /// <param name="builder">The downstream's HTTP client builder.</param>
    /// <param name="downstreamName">Configuration key under <c>DownstreamCredentials</c>; defaults to the client's name.</param>
    public static IHttpClientBuilder AddBasicCredentials(
        this IHttpClientBuilder builder, string? downstreamName = null)
    {
        var name = BindCredentials(
            builder, downstreamName, options => options.HasBasic, "Username and Password");

        return builder.AddHttpMessageHandler(provider => new BasicCredentialsHandler(
            name,
            provider.GetRequiredService<IOptionsMonitor<DownstreamCredentialOptions>>(),
            provider.GetRequiredService<ILogger<BasicCredentialsHandler>>()));
    }

    /// <summary>
    /// Authenticates to this downstream with this API's own API key, from
    /// <c>DownstreamCredentials:{name}:ApiKey</c>, sent in <c>:ApiKeyHeader</c> (default <c>X-Api-Key</c>).
    /// A missing key fails at startup.
    /// <code>
    /// services.AddHttpClient("vendor", c => c.BaseAddress = vendorUri).AddApiKeyCredentials();
    /// </code>
    /// </summary>
    /// <param name="builder">The downstream's HTTP client builder.</param>
    /// <param name="downstreamName">Configuration key under <c>DownstreamCredentials</c>; defaults to the client's name.</param>
    public static IHttpClientBuilder AddApiKeyCredentials(
        this IHttpClientBuilder builder, string? downstreamName = null)
    {
        var name = BindCredentials(builder, downstreamName, options => options.HasApiKey, "ApiKey");

        return builder.AddHttpMessageHandler(provider => new ApiKeyCredentialsHandler(
            name,
            provider.GetRequiredService<IOptionsMonitor<DownstreamCredentialOptions>>(),
            provider.GetRequiredService<ILogger<ApiKeyCredentialsHandler>>()));
    }

    /// <summary>
    /// Binds this downstream's named credentials and fails STARTUP, naming the section, when the values
    /// its method needs are absent — not a 401 from the far end on the first real call.
    /// </summary>
    private static string BindCredentials(
        IHttpClientBuilder builder,
        string? downstreamName,
        Func<DownstreamCredentialOptions, bool> isUsable,
        string required)
    {
        var name = downstreamName ?? builder.Name;
        var section = DownstreamCredentialOptions.SectionFor(name);

        builder.Services.AddOptions<DownstreamCredentialOptions>(name)
            .BindConfiguration(section)
            .Validate(
                isUsable,
                $"'{section}' must set {required} for downstream '{name}' — the section is missing, " +
                "misspelled, or its secret was not supplied in this environment.")
            .ValidateOnStart();

        return name;
    }
}
