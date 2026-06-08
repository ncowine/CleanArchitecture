using BuildingBlocks.Auditing;
using Microsoft.Extensions.Caching.Hybrid;

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
        });

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

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
