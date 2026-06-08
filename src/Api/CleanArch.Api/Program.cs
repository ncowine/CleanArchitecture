using BuildingBlocks.Messaging;
using CleanArch.Api;
using Library.Infrastructure;
using Library.Presentation;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Students.Infrastructure;
using Students.Presentation;

var builder = WebApplication.CreateBuilder(args);

var studentsConnectionString =
    builder.Configuration.GetConnectionString("Students")
    ?? throw new InvalidOperationException("ConnectionStrings:Students is not configured.");

var libraryConnectionString =
    builder.Configuration.GetConnectionString("Library")
    ?? throw new InvalidOperationException("ConnectionStrings:Library is not configured.");

builder.Services
    .AddApiServices()
    .AddApiAuthentication()
    .AddObservability()
    .AddMediator()
    .AddStudentsModule(studentsConnectionString)
    .AddLibraryModule(libraryConnectionString);

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
await app.UseDevelopmentSetupAsync();

app.MapGet("/", () => "Hello World!")
   .WithName("Root")
   .WithSummary("Sanity-check endpoint");

// Readiness (both databases) and liveness (process up, no dependency checks).
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

app.MapStudentEndpoints();
app.MapLibraryEndpoints();

app.Run();
