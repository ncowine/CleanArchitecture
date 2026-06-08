using CleanArch.Api.Authentication;
using Microsoft.AspNetCore.Authentication;

namespace CleanArch.Api;

internal static class AuthenticationExtensions
{
    /// <summary>
    /// Authentication + authorization. The POC authenticates via API key only; after authentication the
    /// principal is enriched from Active Directory (roles/groups). Backing stores are fakes.
    /// <para>
    /// In production, add an Okta JWT bearer scheme alongside the API key and a policy scheme to select
    /// per request (X-Api-Key header -> ApiKey, else -> Okta bearer). Endpoints, the actor, and audit
    /// wiring are unchanged.
    /// </para>
    /// </summary>
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services)
    {
        // FAKE backing stores — replace in production (hashed API-key store; real AD via LDAP or Graph).
        services.AddSingleton<IApiKeyValidator, FakeApiKeyValidator>();
        services.AddSingleton<IUserDirectory, FakeUserDirectory>();

        // Enrich the authenticated principal with Active Directory info (roles/groups) after auth.
        services.AddScoped<IClaimsTransformation, ActiveDirectoryClaimsTransformation>();

        services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationHandler.SchemeName, configureOptions: null);

        services.AddAuthorization();
        return services;
    }
}
