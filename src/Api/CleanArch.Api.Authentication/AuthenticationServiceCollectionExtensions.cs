using System.Runtime.Versioning;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArch.Api.Authentication;

public static class AuthenticationServiceCollectionExtensions
{
    /// <summary>
    /// Authentication + authorization. Up to three schemes coexist behind a policy scheme that selects per
    /// request: an <c>X-Api-Key</c> header → API key (service callers); an <c>Authorization: Bearer</c>
    /// token → Okta JWT (token callers, when <c>Okta:Authority</c> is configured); otherwise → HTTP Basic
    /// validated against Active Directory (interactive callers). After any of them succeeds, the principal
    /// is enriched from AD (display name + roles from group membership) by
    /// <see cref="ActiveDirectoryClaimsTransformation"/>, so the rest of the app sees one fully-populated
    /// principal regardless of how the caller authenticated.
    /// </summary>
    public static IServiceCollection AddApiAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        // API keys are validated against the DATABASE. The keys live in students.db (the primary DB) but
        // behind a dedicated ApiKeyDbContext isolated from the Students domain (its own migrations-history
        // table), so an auth concern never entangles the Students schema. Only SHA-256 hashes are stored —
        // never plaintext. The concrete DB validator is wrapped by a short-TTL caching decorator (the same
        // inner + decorator shape as the AD user directory) because validation runs on every request.
        var apiKeyConnectionString = configuration.GetConnectionString("Students")
            ?? throw new InvalidOperationException("ConnectionStrings:Students is not configured.");
        services.AddDbContext<ApiKeyDbContext>(options =>
            options.UseSqlite(apiKeyConnectionString,
                sqlite => sqlite.MigrationsHistoryTable(ApiKeyDbContext.MigrationsHistoryTable)));
        services.AddScoped<DbApiKeyValidator>();
        services.AddScoped<IApiKeyValidator>(provider => new CachingApiKeyValidator(
            provider.GetRequiredService<DbApiKeyValidator>(),
            provider.GetRequiredService<HybridCache>()));

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

        // Okta JWT is enabled only when configured (the Authority is the OIDC issuer; it supplies the
        // signing keys via discovery). Off by default for the POC, so Bearer tokens get a 401 until set.
        var oktaAuthority = configuration["Okta:Authority"];
        var oktaAudience = configuration["Okta:Audience"];
        var oktaConfigured = !string.IsNullOrWhiteSpace(oktaAuthority);

        const string selectorScheme = "Smart";
        var authBuilder = services.AddAuthentication(selectorScheme)
            // Route each request to the right concrete scheme: an X-Api-Key header → ApiKey; an
            // 'Authorization: Bearer' token → Okta JWT (when configured); otherwise → Basic (the
            // Authorization header used by Swagger's "Authorize" dialog and human callers).
            .AddPolicyScheme(selectorScheme, "ApiKey, Bearer, or Basic", options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    if (context.Request.Headers.ContainsKey(ApiKeyAuthenticationHandler.HeaderName))
                        return ApiKeyAuthenticationHandler.SchemeName;

                    if (oktaConfigured && context.Request.Headers.Authorization.ToString()
                            .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        return JwtBearerDefaults.AuthenticationScheme;

                    return BasicAuthenticationHandler.SchemeName;
                };
            })
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationHandler.SchemeName, configureOptions: null)
            .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>(
                BasicAuthenticationHandler.SchemeName, configureOptions: null);

        if (oktaConfigured)
        {
            authBuilder.AddJwtBearer(options =>
            {
                // VERIFY the token: setting Authority + Audience validates the signature (keys fetched from
                // the Authority's OIDC metadata), the issuer, the audience, and the lifetime on every request.
                options.Authority = oktaAuthority;
                options.Audience = oktaAudience;

                // RESOLVE who the token represents. Okta access tokens carry the user in 'sub' and have no
                // 'name' claim, so without this the principal's Name would be null and neither the audit actor
                // nor the AD role-enrichment could identify the caller. Keep the raw JWT claim names and treat
                // the configured claim (default 'sub') as Name — then ActiveDirectoryClaimsTransformation
                // resolves the real user + roles, exactly as it does for the API-key and Basic schemes.
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = configuration["Okta:NameClaim"] ?? "sub";
            });
        }

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
