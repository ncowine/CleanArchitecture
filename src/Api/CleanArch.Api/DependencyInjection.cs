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
                Description = "College API — multi-database (Students, Staff, Vendors, Alumni)."
            });
        });

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        return services;
    }
}
