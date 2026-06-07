using BuildingBlocks.Outbox;
using Library.Infrastructure.Persistence;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Students.Infrastructure.Persistence;

namespace CleanArch.Api;

internal static class ObservabilityExtensions
{
    public const string ServiceName = "CleanArch.Api";

    /// <summary>
    /// Health checks (both databases) and OpenTelemetry tracing + metrics. The console exporter makes
    /// telemetry visible locally; swap it for the OTLP exporter to ship to a collector/Tempo/Prometheus.
    /// </summary>
    public static IServiceCollection AddObservability(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddDbContextCheck<StudentsDbContext>("students-db")
            .AddDbContextCheck<LibraryDbContext>("library-db");

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddConsoleExporter())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter(OutboxDiagnostics.MeterName)
                .AddConsoleExporter());

        return services;
    }
}
