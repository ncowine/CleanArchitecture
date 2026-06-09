using System.IO.Compression;
using System.Text.Json.Serialization;
using Asp.Versioning;
using BuildingBlocks.Auditing;
using CleanArch.Api.Authentication;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.OpenApi;

namespace CleanArch.Api;


internal static class DependencyInjection
{
    /// <summary>
    /// Registers the API host's own concerns: OpenAPI/Swagger, RFC 7807 problem details, and the
    /// global exception handler that turns expected exceptions into proper responses.
    /// </summary>
    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new()
            {
                Title = "CleanArchitecture API",
                Version = "v1",
                Description = "College API — database-per-domain modular monolith (Students + Library)."
            });

            // Vertical-slice requests are nested (e.g. CreateStudent.Command, BorrowBook.Command), so the
            // default schema id (the short type name "Command") collides. Qualify with the declaring type.
            options.CustomSchemaIds(SchemaId);

            // Three ways to authorize in Swagger UI. Each is a separate security requirement, so they are
            // alternatives (Basic OR ApiKey OR Bearer), matching the runtime policy scheme.

            // Human callers: AD username + password (HTTP Basic) — renders user/password fields.
            options.AddSecurityDefinition(AuthenticationSchemes.Basic, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "basic",
                Description = "Active Directory username and password (HTTP Basic). Send over HTTPS only."
            });

            // Token callers: an Okta access token (JWT). Active only when Okta is configured.
            options.AddSecurityDefinition(AuthenticationSchemes.Bearer, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Okta access token (JWT). Paste the raw token — Swagger adds the 'Bearer ' prefix."
            });

            // Service callers: X-Api-Key header.
            options.AddSecurityDefinition(AuthenticationSchemes.ApiKey, new OpenApiSecurityScheme
            {
                Name = AuthenticationSchemes.ApiKeyHeaderName,
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Description = $"Service-to-service API key, sent in the '{AuthenticationSchemes.ApiKeyHeaderName}' header."
            });

            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(AuthenticationSchemes.Basic, document, null)] = new List<string>()
            });

            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(AuthenticationSchemes.ApiKey, document, null)] = new List<string>()
            });

            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(AuthenticationSchemes.Bearer, document, null)] = new List<string>()
            });
        });

        // API versioning. House preference is clean URLs, so the version travels in an 'api-version'
        // HEADER (not the path or query). A default version is assumed when the header is absent, so
        // existing callers — including the WPF client — keep working untouched. ReportApiVersions adds
        // 'api-supported-versions'/'api-deprecated-versions' response headers so clients can discover what's
        // available. The ApiExplorer group name ("v1") buckets operations into the matching SwaggerDoc.
        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions = true;
            options.ApiVersionReader = new HeaderApiVersionReader("api-version");
        })
        .AddApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'VVV";
        });

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // Trim the wire payload. Don't ship null-valued properties (e.g. ReturnedOn, Error) — they're
        // dead weight on every row of a list response. Applies to all minimal-API JSON results.
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

        // Compress responses (Brotli preferred, Gzip fallback). JSON compresses ~80–90%, the single
        // biggest win for large list payloads. Level = Fastest: near-optimal ratio for dynamic content
        // without the CPU/latency cost of Optimal — the goal here is compact AND quick.
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
        });
        services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
        services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);

        // Audit actor: the authenticated user, else the dev X-Actor header, else "system". Registered
        // after AddMediator's default SystemActor, so this wins.
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentActor, HttpContextActor>();

        // Caching. HybridCache is in-memory only today (no Redis dependency). It is the seam for going
        // distributed later: add Microsoft.Extensions.Caching.StackExchangeRedis +
        // services.AddStackExchangeRedisCache(...) and HybridCache automatically uses Redis as its L2 —
        // no changes to the decorator, handlers, or call sites. Entries are kept short-lived and small
        // so the in-memory footprint stays modest.
        services.AddHybridCache(options =>
        {
            options.MaximumPayloadBytes = 64 * 1024; // 64 KB per entry — these are tiny reference objects
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),       // overall lifetime (and L2 lifetime once Redis is added)
                LocalCacheExpiration = TimeSpan.FromMinutes(1), // in-memory (L1) lifetime — short, to bound memory
            };
        });

        return services;
    }

    // Unique, readable OpenAPI schema id: prefix nested types with their declaring type
    // (CreateStudent.Command -> "CreateStudentCommand") and expand generics
    // (PagedResult<GetStudent.Response> -> "PagedResultOfGetStudentResponse").
    private static string SchemaId(Type type)
    {
        var prefix = type.DeclaringType is null ? string.Empty : SchemaId(type.DeclaringType);

        if (!type.IsGenericType)
        {
            return prefix + type.Name;
        }

        var name = type.Name[..type.Name.IndexOf('`')];
        var arguments = string.Concat(type.GetGenericArguments().Select(SchemaId));
        return $"{prefix}{name}Of{arguments}";
    }
}
