using System.Diagnostics;
using BuildingBlocks.Outbox;
using CleanArch.Api.Authentication;
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
    ///   • traces  -> Tempo over OTLP/gRPC   (the app PUSHES) — HTTP in, HTTP out, and every EF query
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

        // Optional headers sent with every OTLP export, in "key=value,key2=value2" form.
        //
        // Empty for local development, where the collector is on localhost and there is nothing to
        // authenticate to. They exist for the deployment where the app and the telemetry stores are on
        // DIFFERENT hosts: Tempo and Loki have no authentication of their own, so the only thing that can
        // stop a stranger writing into your traces and logs is a credential checked by whatever proxy
        // fronts them. Typically:
        //     Observability__Tempo__Headers = "Authorization=Basic <base64 of user:password>"
        //
        // SECRET — supply from an environment variable or a secret store, never from appsettings.json.
        var tempoHeaders = configuration["Observability:Tempo:Headers"];
        var lokiHeaders = configuration["Observability:Loki:Headers"];

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
                // Token exchanges for On-Behalf-Of downstream calls. Without this the IdP round-trip is
                // invisible and reads as unexplained latency on the downstream span.
                .AddSource(OnBehalfOfDiagnostics.ActivitySourceName)
                // A child span per database query. Without it a slow handler is one opaque block: you can
                // see the request took four seconds but not that 3.9 of them were a single SELECT — or
                // that it was forty small queries in a loop, which is a different bug with a different fix.
                .AddEntityFrameworkCoreInstrumentation(ef =>
                {
                    // Record a query only when it belongs to a trace that already exists.
                    //
                    // Without this, every poll of the outbox dispatcher — a SELECT every two seconds,
                    // forever — becomes a ROOT span, and therefore a whole trace of its own. Measured on
                    // an idle instance: ~38,000 traces a day, each one a single SELECT that answers no
                    // question anybody asked. They crowd out the traces that matter and they are the bulk
                    // of what Tempo would be paid to store.
                    //
                    // Note the PARENT. This callback runs after the instrumentation has already started
                    // its own span and made it current, so Activity.Current is never null here — it is the
                    // query's own span. What distinguishes a request from background work is whether that
                    // span has a parent: inside a request it is the ASP.NET Core server span, and for a
                    // background poll there is nothing above it at all.
                    //
                    // If a background operation is later given a span of its own (see OnBehalfOfDiagnostics
                    // for the shape), its queries acquire a parent and start being recorded again
                    // automatically — which is the behaviour you want.
                    ef.Filter = (_, _) => Activity.Current?.Parent is not null;
                })
                // Traces -> Tempo (OTLP/gRPC, :4317). gRPC uses no URL path, so the endpoint is used as-is.
                .AddOtlpExporter(otlp =>
                {
                    otlp.Endpoint = new Uri(tempoEndpoint);
                    otlp.Protocol = OtlpExportProtocol.Grpc;

                    if (!string.IsNullOrWhiteSpace(tempoHeaders))
                    {
                        otlp.Headers = tempoHeaders;
                    }
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

                    if (!string.IsNullOrWhiteSpace(lokiHeaders))
                    {
                        otlp.Headers = lokiHeaders;
                    }
                }));

        return services;
    }
}
