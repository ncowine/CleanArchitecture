using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Auditing.Elasticsearch;

public static class DependencyInjection
{
    /// <summary>
    /// Ships audit records to Elasticsearch (for Kibana). Registers a background bulk shipper and points the
    /// audit sink at it — overriding the default logging sink. If <c>Audit:Elasticsearch:Uri</c> is not
    /// configured, this is a no-op and auditing keeps using the logging sink, so it's safe by default.
    /// </summary>
    public static IServiceCollection AddElasticsearchAudit(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(ElasticsearchAuditOptions.SectionName);
        var options = new ElasticsearchAuditOptions();
        section.Bind(options);

        if (string.IsNullOrWhiteSpace(options.Uri))
        {
            // Not configured — leave the default logging audit sink in place. Safe no-op.
            return services;
        }

        services.Configure<ElasticsearchAuditOptions>(section);

        services.AddSingleton(_ =>
        {
            var settings = new ElasticsearchClientSettings(new Uri(options.Uri));

            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                settings = settings.Authentication(new ApiKey(options.ApiKey));
            }
            else if (!string.IsNullOrWhiteSpace(options.Username))
            {
                settings = settings.Authentication(new BasicAuthentication(options.Username, options.Password ?? string.Empty));
            }

            return new ElasticsearchClient(settings);
        });

        services.AddSingleton(new AuditShipmentQueue(options.QueueCapacity));
        services.AddHostedService<ElasticsearchAuditShipper>();

        // Last-wins over the default LoggingAuditSink registered (via TryAdd) in AddMediator.
        services.AddScoped<IAuditSink, ElasticsearchAuditSink>();

        return services;
    }
}
