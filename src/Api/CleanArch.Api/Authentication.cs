using System.Security.Claims;
using System.Text;
using CleanArch.Api.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CleanArch.Api;

internal static class AuthenticationExtensions
{
    /// <summary>
    /// Dual authentication — a bearer token (Okta in production; a symmetric dev token here) OR an API
    /// key — selected per request by a policy scheme. After either authenticates, the principal is
    /// enriched from Active Directory. The backing stores are fakes for the POC; swap them and point the
    /// JWT at Okta in production with no change to endpoints or the actor/audit wiring.
    /// </summary>
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection("Jwt");
        var signingKey = jwt["SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey is not configured.");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));

        // FAKE backing stores — replace in production (hashed API-key store; real AD via LDAP or Graph).
        services.AddSingleton<IApiKeyValidator, FakeApiKeyValidator>();
        services.AddSingleton<IUserDirectory, FakeUserDirectory>();

        // After either scheme authenticates, enrich the principal with Active Directory info (roles, etc.).
        services.AddScoped<IClaimsTransformation, ActiveDirectoryClaimsTransformation>();

        const string smartScheme = "Smart";
        services.AddAuthentication(smartScheme)
            // Per request: API key if the header is present, otherwise the bearer token.
            .AddPolicyScheme(smartScheme, "Okta JWT or API key", options =>
            {
                options.ForwardDefaultSelector = context =>
                    context.Request.Headers.ContainsKey(ApiKeyAuthenticationHandler.HeaderName)
                        ? ApiKeyAuthenticationHandler.SchemeName
                        : JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                // POC: validate a symmetric dev token. In production point this at Okta instead:
                //   options.Authority = configuration["Okta:Authority"];
                //   options.Audience  = configuration["Okta:Audience"];
                // Okta signs RS256 and the handler fetches its JWKS automatically — no symmetric key.
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt["Issuer"],
                    ValidateAudience = true,
                    ValidAudience = jwt["Audience"],
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateLifetime = true,
                    NameClaimType = ClaimTypes.Name,
                    RoleClaimType = ClaimTypes.Role,
                };
            })
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationHandler.SchemeName, configureOptions: null);

        services.AddAuthorization();
        return services;
    }
}

internal sealed record DevTokenRequest(string Actor, string[]? Roles);

/// <summary>DEV-ONLY: mints a signed JWT so the API can be exercised without a real identity provider.</summary>
internal static class DevTokenFactory
{
    public static string Create(IConfiguration configuration, string actor, IEnumerable<string> roles)
    {
        var jwt = configuration.GetSection("Jwt");
        var signingKey = jwt["SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey is not configured.");
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256);

        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim(ClaimTypes.Name, actor));
        identity.AddClaim(new Claim("sub", actor));
        foreach (var role in roles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt["Issuer"],
            Audience = jwt["Audience"],
            Subject = identity,
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = credentials,
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
