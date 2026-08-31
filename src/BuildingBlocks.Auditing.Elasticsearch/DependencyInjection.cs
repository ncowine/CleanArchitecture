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
    /// <remarks>
    /// <para>
    /// <b>Apply <c>audit-index-template.json</c> (next to this file) before shipping to a real cluster.</b>
    /// It maps <c>details</c> as <c>flattened</c>. Without it, Elasticsearch maps every distinct annotation
    /// key as its own field, and an index that reaches the 1000-field limit starts <i>rejecting</i>
    /// records — which this sink logs and drops, so the trail would thin out silently.
    /// </para>
    /// <para>
    /// It is applied as an operations step rather than at startup on purpose: the shipping account is meant
    /// to be write-only to <c>cleanarch-audit-*</c>, and creating templates needs cluster privileges that
    /// an append-only pipe should not hold.
    /// </para>
    /// </remarks>
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
