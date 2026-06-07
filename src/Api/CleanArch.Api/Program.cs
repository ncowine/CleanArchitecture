using BuildingBlocks.Messaging;
using CleanArch.Api;
using Library.Infrastructure;
using Library.Presentation;
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
    .AddMediator()
    .AddStudentsModule(studentsConnectionString)
    .AddLibraryModule(libraryConnectionString);

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
await app.UseDevelopmentSetupAsync();

app.MapGet("/", () => "Hello World!")
   .WithName("Root")
   .WithSummary("Sanity-check endpoint");

app.MapStudentEndpoints();
app.MapLibraryEndpoints();

app.Run();
