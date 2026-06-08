using System.Security.Claims;

namespace CleanArch.Api.Authentication;

public interface IApiKeyValidator
{
    /// <summary>Returns the identity for a valid key, or null if the key is unknown.</summary>
    ClaimsIdentity? Validate(string apiKey);
}

/// <summary>
/// FAKE: in-memory API keys mapped to service identities. In production, validate against a hashed key
/// store (never plaintext) and map each key to a real service principal.
/// </summary>
internal sealed class FakeApiKeyValidator : IApiKeyValidator
{
    private static readonly Dictionary<string, string> KeysToService = new(StringComparer.Ordinal)
    {
        ["dev-api-key-reporting"] = "reporting-service",
        ["dev-api-key-integration"] = "integration-service",
    };

    public ClaimsIdentity? Validate(string apiKey)
    {
        if (!KeysToService.TryGetValue(apiKey, out var serviceName))
        {
            return null;
        }

        var identity = new ClaimsIdentity(ApiKeyAuthenticationHandler.SchemeName);
        identity.AddClaim(new Claim(ClaimTypes.Name, serviceName));
        identity.AddClaim(new Claim(ClaimTypes.Role, "service"));
        return identity;
    }
}
