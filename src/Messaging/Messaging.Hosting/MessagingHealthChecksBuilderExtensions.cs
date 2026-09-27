using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Messaging.Hosting
{
    public static class MessagingHealthChecksBuilderExtensions
    {
        /// <summary>Adds a health check over every bus added with <see cref="MessagingServiceCollectionExtensions.AddMessaging"/>.</summary>
        public static IHealthChecksBuilder AddMessaging(this IHealthChecksBuilder builder, string name = "messaging", IEnumerable<string> tags = null)
        {
            return builder.AddCheck<MessagingHealthCheck>(name, HealthStatus.Unhealthy, tags ?? new[] { "ready" });
        }
    }
}
