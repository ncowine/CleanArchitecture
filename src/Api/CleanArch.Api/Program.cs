using Asp.Versioning;
using Asp.Versioning.Builder;
using BuildingBlocks.Auditing.Elasticsearch;
using BuildingBlocks.Messaging;
using BuildingBlocks.RealTime;
using CleanArch.Api;
using CleanArch.Api.Authentication;
using CleanArch.Api.Realtime;
using Library.Infrastructure;
using Library.Presentation;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Students.Infrastructure;
using Students.Presentation;
using TestPlans.Infrastructure;
using TestPlans.Presentation;
using TesterGuide.Infrastructure;
using TesterGuide.Presentation;

var builder = WebApplication.CreateBuilder(args);

var studentsConnectionString = RequireConnectionString("Students");
var libraryConnectionString = RequireConnectionString("Library");
var testPlansConnectionString = RequireConnectionString("TestPlans");
var testerGuideConnectionString = RequireConnectionString("TesterGuide");

string RequireConnectionString(string name) =>
    builder.Configuration.GetConnectionString(name) is { } value && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidOperationException(
            $"ConnectionStrings:{name} is not configured. Set it via ConnectionStrings__{name} " +
            "(env var / IIS app pool), user-secrets, or appsettings.");

builder.Services
    .AddApiServices()
    .AddReverseProxySupport(builder.Configuration)
    .AddApiAuthentication(builder.Configuration)
    .AddOnBehalfOf(builder.Configuration)
    .AddApiRateLimiting(builder.Configuration)
    .AddApiCors(builder.Configuration)
    .AddObservability(builder.Configuration)
    // Ship audit records to Elasticsearch (viewed in Kibana). No-op if Audit:Elasticsearch:Uri is unset,
    // in which case auditing stays on the logging sink.
    .AddElasticsearchAudit(builder.Configuration)
    .AddMediator()
    // Registered after the mediator and before the modules, so the post-commit realtime dispatch behavior
    // sits outside each module's transaction behavior (its flush runs after the commit).
    .AddRealtimeDispatch()
    .AddStudentsModule(studentsConnectionString)
    .AddLibraryModule(libraryConnectionString)
    .AddTestPlansModule(testPlansConnectionString)
    .AddTesterGuideModule(testerGuideConnectionString);

// Real-time transport (SignalR) — overrides the kit's no-op notifier and hosts the presence hub.
builder.Services.AddSignalR();
builder.Services.AddScoped<IRealtimeNotifier, SignalRRealtimeNotifier>();

// Example: downstream API clients that call AS the authenticated user (OAuth2 On-Behalf-Of). AddOnBehalfOf
// exchanges the caller's OIDC token for one scoped to THAT downstream and attaches it. One line per
// downstream; each reads its audience from OnBehalfOf:Downstreams:<client name>, so several downstreams
// each get a token their own service accepts.
//   builder.Services.AddHttpClient("billing", client =>
//           client.BaseAddress = new Uri(builder.Configuration["Downstreams:Billing:BaseUrl"]!))
//       .AddOnBehalfOf();
//   builder.Services.AddHttpClient("grading", client =>
//           client.BaseAddress = new Uri(builder.Configuration["Downstreams:Grading:BaseUrl"]!))
//       .AddOnBehalfOf();

var app = builder.Build();

// ── One-shot operator command, not a server run ─────────────────────────────────────────────────
// Mints a production API key, stores only its hash, prints the raw key to stdout ONCE and exits:
//   docker compose run --rm api --mint-api-key=reporting-service --mint-api-key-roles=service
// This is the supported way to create real API keys. The well-known dev keys (dev-api-key-*) are
// seeded only in Development precisely because they are published in the docs.
// Note the "--key=value" form: WebApplicationBuilder's command-line configuration provider rejects
// bare positional arguments, so the values are read back out of configuration rather than parsed.
if (app.Configuration["mint-api-key"] is { Length: > 0 } mintSubject)
{
    using var mintScope = app.Services.CreateScope();
    var mintedKey = await ApiKeyStoreSetup.MintAsync(
        mintScope.ServiceProvider,
        mintSubject,
        app.Configuration["mint-api-key-roles"] ?? "service");

    Console.WriteLine(mintedKey);
    return;
}

// FIRST in the pipeline: rewrites RemoteIpAddress and the scheme from the proxy's X-Forwarded-*
// headers before anything downstream reads them — correlation ids, auth, the per-IP rate limiter and
// the audit actor all depend on seeing the real caller. No-op unless Proxy:Enabled is true.
app.UseForwardedHeaders();

app.UseResponseCompression();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter(); // after auth, so the limiter can partition by the authenticated principal
await app.UseDatabaseMigrationsAsync();
await app.UseDevelopmentSetupAsync();

app.MapGet("/", () => "Hello World!")
   .WithName("Root")
   .WithSummary("Sanity-check endpoint")
   .WithTags("System");

// Readiness (both databases) and liveness (process up, no dependency checks). Exempt from the rate
// limiter so orchestrator probes are never throttled (they share one source IP).
app.MapHealthChecks("/health").DisableRateLimiting();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).DisableRateLimiting();

// Prometheus PULLS metrics from here every few seconds. Exempt from the rate limiter — the scraper
// hits it repeatedly from a single source.
//
// Anonymous by default, which is right when Prometheus and the app share a host. When the scraper is
// on a DIFFERENT machine the endpoint is exposed to whatever network sits between them, and it is not
// harmless: it enumerates every route, request rate and error count in the service. Set
// Observability:Metrics:RequireAuthentication to make the scraper present a credential (an API key in
// X-Api-Key). Network-level restriction is still worth doing either way — this is defence in depth,
// not a replacement for it.
var metricsEndpoint = app.MapPrometheusScrapingEndpoint().DisableRateLimiting();

if (app.Configuration.GetValue<bool>("Observability:Metrics:RequireAuthentication"))
{
    metricsEndpoint.RequireAuthorization();
}

// One version set (v1) shared by both modules. Each module attaches it to its endpoint groups.
ApiVersionSet versionSet = app.NewApiVersionSet()
    .HasApiVersion(new ApiVersion(1, 0))
    .ReportApiVersions()
    .Build();

app.MapStudentEndpoints(versionSet);
app.MapAcademicEndpoints(versionSet);
app.MapBillingEndpoints(versionSet);
app.MapLibraryEndpoints(versionSet);
app.MapTestPlanEndpoints(versionSet);
app.MapTesterGuideEndpoints(versionSet);

// Real-time presence + notifications hub (live view + "someone actioned this"). Exempt from the rate
// limiter — connections are long-lived and share a source.
app.MapHub<PresenceHub>("/hubs/presence").DisableRateLimiting();

app.Run();
