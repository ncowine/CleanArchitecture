using Asp.Versioning;
using Asp.Versioning.Builder;
using BuildingBlocks.Messaging;
using CleanArch.Api;
using CleanArch.Api.Authentication;
using Library.Infrastructure;
using Library.Presentation;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Students.Infrastructure;
using Students.Presentation;

var builder = WebApplication.CreateBuilder(args);

var studentsConnectionString = RequireConnectionString("Students");
var libraryConnectionString = RequireConnectionString("Library");

string RequireConnectionString(string name) =>
    builder.Configuration.GetConnectionString(name) is { } value && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidOperationException(
            $"ConnectionStrings:{name} is not configured. Set it via ConnectionStrings__{name} " +
            "(env var / IIS app pool), user-secrets, or appsettings.");

builder.Services
    .AddApiServices()
    .AddApiAuthentication(builder.Configuration)
    .AddOnBehalfOf(builder.Configuration)
    .AddApiRateLimiting(builder.Configuration)
    .AddApiCors(builder.Configuration)
    .AddObservability()
    .AddMediator()
    .AddStudentsModule(studentsConnectionString)
    .AddLibraryModule(libraryConnectionString);

// Example: a downstream API client that calls AS the authenticated user (OAuth2 On-Behalf-Of). The
// OnBehalfOfHandler exchanges the caller's OIDC token for a downstream-scoped token and attaches it.
// Point the base address at the real downstream API and inject this client where you call out.
//   builder.Services.AddHttpClient("Downstream", client =>
//           client.BaseAddress = new Uri(builder.Configuration["DownstreamApi:BaseUrl"]!))
//       .AddHttpMessageHandler<OnBehalfOfHandler>();

var app = builder.Build();

app.UseResponseCompression();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter(); // after auth, so the limiter can partition by the authenticated principal
await app.UseDevelopmentSetupAsync();

app.MapGet("/", () => "Hello World!")
   .WithName("Root")
   .WithSummary("Sanity-check endpoint")
   .WithTags("System");

// Readiness (both databases) and liveness (process up, no dependency checks). Exempt from the rate
// limiter so orchestrator probes are never throttled (they share one source IP).
app.MapHealthChecks("/health").DisableRateLimiting();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).DisableRateLimiting();

// One version set (v1) shared by both modules. Each module attaches it to its endpoint groups.
ApiVersionSet versionSet = app.NewApiVersionSet()
    .HasApiVersion(new ApiVersion(1, 0))
    .ReportApiVersions()
    .Build();

app.MapStudentEndpoints(versionSet);
app.MapAcademicEndpoints(versionSet);
app.MapBillingEndpoints(versionSet);
app.MapLibraryEndpoints(versionSet);

app.Run();
