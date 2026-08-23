using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace CleanArch.Api;

/// <summary>
/// Support for running behind a reverse proxy (Caddy/nginx/an ingress controller). The proxy terminates
/// TLS and forwards each request over the internal network, so without this the app sees the PROXY as the
/// client: <c>Connection.RemoteIpAddress</c> is the proxy's address and the scheme is plain http.
///
/// That matters here for two concrete reasons:
///   • the rate limiter partitions anonymous callers by IP (<see cref="RateLimitingAndCorsExtensions"/>) —
///     with every request arriving from one proxy address they would all share a single bucket, so one
///     noisy client throttles everybody and the per-caller limit protects nothing;
///   • audit records and logs would attribute every action to the proxy instead of the real caller.
///
/// OFF unless <c>Proxy:Enabled</c> is true, so direct-to-Kestrel setups (local dev, IIS) are unchanged.
///
/// SECURITY: <c>X-Forwarded-For</c> is an ordinary request header — anyone who can reach Kestrel directly
/// can forge one and spoof their source IP. It is only trustworthy because the middleware ignores it from
/// anywhere except the addresses named in <c>Proxy:KnownNetworks</c>/<c>Proxy:KnownProxies</c>. Never
/// enable this while the app's port is also published to the outside world.
/// </summary>
internal static class ReverseProxyExtensions
{
    public static IServiceCollection AddReverseProxySupport(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("Proxy:Enabled"))
        {
            return services;
        }

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;

            // The defaults trust only loopback, which is never right in a container — the proxy is a peer
            // on the Docker network, not localhost. Clear them and re-add exactly what config names, so an
            // empty/missing config fails closed (headers ignored) rather than open.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();

            foreach (var cidr in configuration.GetSection("Proxy:KnownNetworks").Get<string[]>() ?? [])
            {
                if (string.IsNullOrWhiteSpace(cidr))
                {
                    continue;
                }

                options.KnownIPNetworks.Add(ParseNetwork(cidr));
            }

            foreach (var proxy in configuration.GetSection("Proxy:KnownProxies").Get<string[]>() ?? [])
            {
                if (!string.IsNullOrWhiteSpace(proxy) && IPAddress.TryParse(proxy, out var address))
                {
                    options.KnownProxies.Add(address);
                }
            }

            // How many proxy hops to unwind. One proxy in front => 1. Raise it only if you genuinely run
            // a chain (e.g. a CDN in front of Caddy); too high lets a caller inject extra hops.
            options.ForwardLimit = configuration.GetValue<int?>("Proxy:ForwardLimit") ?? 1;
        });

        return services;
    }

    private static System.Net.IPNetwork ParseNetwork(string cidr)
    {
        if (!System.Net.IPNetwork.TryParse(cidr, out var network))
        {
            throw new InvalidOperationException(
                $"Proxy:KnownNetworks contains '{cidr}', which is not a CIDR range like 172.16.0.0/12.");
        }

        return network;
    }
}
