using BuildingBlocks.Outbox;
using Library.Infrastructure.Persistence;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Students.Infrastructure.Persistence;

namespace CleanArch.Api;

internal static class ObservabilityExtensions
{
    public const string ServiceName = "CleanArch.Api";

    /// <summary>
    /// Health checks (both databases) plus OpenTelemetry for all three signals, wired to a local
    /// Grafana stack:
    ///   • traces  -> Tempo over OTLP/gRPC   (the app PUSHES)
    ///   • logs    -> Loki  over OTLP/HTTP   (the app PUSHES)
    ///   • metrics -> a /metrics page that Prometheus PULLS (mapped in Program.cs)
    /// Endpoints default to the local dev binaries; override via the "Observability" config section.
    /// </summary>
    public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHealthChecks()
            .AddDbContextCheck<StudentsDbContext>("students-db")
            .AddDbContextCheck<LibraryDbContext>("library-db");

        // Push targets. Defaults hit the local Tempo/Loki dev binaries on localhost.
        var tempoEndpoint = configuration["Observability:Tempo:OtlpEndpoint"] ?? "http://localhost:4317";
        var lokiEndpoint = configuration["Observability:Loki:OtlpEndpoint"] ?? "http://localhost:3100/otlp/v1/logs";

        // Make log bodies human-readable in Loki (rendered message + scopes as attributes) instead of
        // raw message templates.
        services.Configure<OpenTelemetryLoggerOptions>(options =>
        {
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
        });

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                // Traces -> Tempo (OTLP/gRPC, :4317). gRPC uses no URL path, so the endpoint is used as-is.
                .AddOtlpExporter(otlp =>
                {
                    otlp.Endpoint = new Uri(tempoEndpoint);
                    otlp.Protocol = OtlpExportProtocol.Grpc;
                }))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                // Built-in .NET runtime metrics (GC, heap, thread pool, CPU) — no extra package needed.
                .AddMeter("System.Runtime")
                .AddMeter(OutboxDiagnostics.MeterName)
                // Metrics are PULLED by Prometheus from /metrics (see MapPrometheusScrapingEndpoint).
                .AddPrometheusExporter())
            .WithLogging(logging => logging
                // Logs -> Loki (OTLP/HTTP). The endpoint already carries the /v1/logs signal path, so the
                // exporter posts to it verbatim.
                .AddOtlpExporter(otlp =>
                {
                    otlp.Endpoint = new Uri(lokiEndpoint);
                    otlp.Protocol = OtlpExportProtocol.HttpProtobuf;
                }));

        return services;
    }
}
