using System.Runtime.Versioning;
using CleanArch.Api.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Hybrid;

namespace CleanArch.Api;

internal static class AuthenticationExtensions
{
    /// <summary>
    /// Authentication + authorization. Two schemes coexist behind a policy scheme that selects per request:
    /// interactive callers send HTTP Basic credentials (validated against Active Directory by an LDAP bind),
    /// service callers send an <c>X-Api-Key</c> header. After either succeeds, the principal is enriched
    /// from AD (display name + roles from group membership) by <see cref="ActiveDirectoryClaimsTransformation"/>,
    /// so the rest of the app sees one fully-populated principal regardless of how the caller authenticated.
    /// </summary>
    public static IServiceCollection AddApiAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        // API-key backing store is a FAKE (replace with a hashed key store).
        services.AddSingleton<IApiKeyValidator, FakeApiKeyValidator>();

        // Active Directory does both halves now: the credential bind (authentication) AND the directory
        // lookup (authorization — display name + group memberships → roles). Both are Windows-only
        // (System.DirectoryServices.AccountManagement), so they sit behind a platform guard that also
        // satisfies the CA1416 analyzer; a non-Windows run falls back to the in-memory fakes so it still boots.
        services.Configure<ActiveDirectoryOptions>(configuration.GetSection(ActiveDirectoryOptions.SectionName));
        if (OperatingSystem.IsWindows())
        {
            AddActiveDirectory(services);
        }
        else
        {
            services.AddSingleton<IUserDirectory, FakeUserDirectory>();
        }

        // Enrich the authenticated principal with Active Directory info (roles/groups) after auth.
        services.AddScoped<IClaimsTransformation, ActiveDirectoryClaimsTransformation>();

        const string selectorScheme = "Smart";
        services.AddAuthentication(selectorScheme)
            // Route each request to the right concrete scheme: an X-Api-Key header → ApiKey, otherwise
            // → Basic (the Authorization header used by Swagger's "Authorize" dialog and human callers).
            .AddPolicyScheme(selectorScheme, "ApiKey or Basic", options =>
            {
                options.ForwardDefaultSelector = context =>
                    context.Request.Headers.ContainsKey(ApiKeyAuthenticationHandler.HeaderName)
                        ? ApiKeyAuthenticationHandler.SchemeName
                        : BasicAuthenticationHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationHandler.SchemeName, configureOptions: null)
            .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>(
                BasicAuthenticationHandler.SchemeName, configureOptions: null);

        services.AddAuthorization();
        return services;
    }

    // Windows-only AD registrations factored out so the platform analyzer (CA1416) is satisfied: this
    // method is marked windows-supported and is only ever called from inside an OperatingSystem.IsWindows()
    // guard. The guard's flow analysis does NOT reach into factory lambdas, so keeping the registrations
    // (and their lambdas) inside an annotated method is what keeps them in a Windows context.
    [SupportedOSPlatform("windows")]
    private static void AddActiveDirectory(IServiceCollection services)
    {
        // Authentication: real credential bind.
        services.AddSingleton<ICredentialValidator, ActiveDirectoryCredentialValidator>();

        // Authorization: directory lookup (display name + groups → roles), cached (a hit per request is
        // costly) — concrete inner + caching decorator, the same pattern as the Students module.
        services.AddScoped<ActiveDirectoryUserDirectory>();
        services.AddScoped<IUserDirectory>(provider => new CachingUserDirectory(
            provider.GetRequiredService<ActiveDirectoryUserDirectory>(),
            provider.GetRequiredService<HybridCache>()));
    }
}
