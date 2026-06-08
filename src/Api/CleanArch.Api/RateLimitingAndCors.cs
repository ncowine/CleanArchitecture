using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace CleanArch.Api;

/// <summary>
/// Edge protection for the host: a global rate limiter (per caller) and a CORS policy (config-driven).
/// Both are wired in <c>Program.cs</c> via <c>UseRateLimiter()</c> / <c>UseCors()</c>.
/// </summary>
internal static class RateLimitingAndCorsExtensions
{
    /// <summary>
    /// A global fixed-window limiter partitioned per caller, so one noisy client can't exhaust the budget
    /// for everyone. Limits are config-driven (<c>RateLimiting:*</c>) with sane defaults. Rejected requests
    /// get 429 plus a <c>Retry-After</c> header so clients can back off.
    /// </summary>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var permitLimit = configuration.GetValue<int?>("RateLimiting:PermitLimit") ?? 100;
        var windowSeconds = configuration.GetValue<int?>("RateLimiting:WindowSeconds") ?? 10;
        var queueLimit = configuration.GetValue<int?>("RateLimiting:QueueLimit") ?? 0;

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                // Partition by the authenticated principal; fall back to the caller's IP for anonymous
                // requests. Runs after authentication in the pipeline, so User is populated here.
                var partitionKey = context.User.Identity?.IsAuthenticated == true
                    ? context.User.Identity!.Name ?? "authenticated"
                    : context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromSeconds(windowSeconds),
                    QueueLimit = queueLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                });
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
                }

                await context.HttpContext.Response.WriteAsync(
                    "Too many requests. Please retry later.", cancellationToken);
            };
        });

        return services;
    }

    /// <summary>
    /// A default CORS policy whose allowed origins come from config (<c>Cors:AllowedOrigins</c>). With none
    /// configured the policy allows nothing — cross-origin browser calls are denied by default (the desktop
    /// client isn't a browser and is unaffected). Add origins per environment to enable a browser SPA.
    /// </summary>
    public static IServiceCollection AddApiCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                if (origins.Length == 0)
                {
                    return; // No origins configured — deny all cross-origin requests.
                }

                policy.WithOrigins(origins)
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        return services;
    }
}
