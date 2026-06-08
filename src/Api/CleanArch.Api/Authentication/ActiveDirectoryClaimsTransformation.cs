using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace CleanArch.Api.Authentication;

/// <summary>
/// Enriches the authenticated principal with info resolved from Active Directory (display name + roles
/// from group membership). Runs after authentication for BOTH schemes (Basic/AD and API key), so the
/// rest of the app sees one fully-populated principal regardless of how the caller authenticated.
/// </summary>
internal sealed class ActiveDirectoryClaimsTransformation : IClaimsTransformation
{
    private const string EnrichedMarker = "ad:enriched";

    private readonly IUserDirectory _directory;
    public ActiveDirectoryClaimsTransformation(IUserDirectory directory) => _directory = directory;

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not { IsAuthenticated: true })
        {
            return principal;
        }

        // IClaimsTransformation can run more than once per request — enrich only once.
        if (principal.HasClaim(claim => claim.Type == EnrichedMarker))
        {
            return principal;
        }

        var principalName = principal.Identity.Name
            ?? principal.FindFirst("upn")?.Value
            ?? principal.FindFirst(ClaimTypes.Email)?.Value;
        if (principalName is null)
        {
            return principal;
        }

        var user = await _directory.FindAsync(principalName, CancellationToken.None);
        if (user is null)
        {
            return principal;
        }

        var enriched = new ClaimsIdentity();
        enriched.AddClaim(new Claim(EnrichedMarker, "true"));
        enriched.AddClaim(new Claim("displayName", user.DisplayName));
        foreach (var role in user.Roles)
        {
            enriched.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        principal.AddIdentity(enriched);
        return principal;
    }
}
