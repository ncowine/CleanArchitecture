using System.Security.Cryptography;
using System.Text;

namespace CleanArch.Api.Authentication;

/// <summary>
/// Hashing and minting for API keys. Keys are high-entropy random secrets, so a fast cryptographic hash
/// (SHA-256) is the correct choice — the slow password hashes (bcrypt/Argon2/PBKDF2) exist to throttle
/// brute-forcing of LOW-entropy human passwords and are unnecessary here. Only the hash is persisted;
/// the raw key is shown to the caller exactly once at creation.
/// </summary>
internal static class ApiKeyHasher
{
    private const string KeyPrefix = "ca_live_";

    /// <summary>Uppercase-hex SHA-256 of the raw key. Deterministic, so it doubles as the lookup value.</summary>
    public static string Hash(string rawKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)));

    /// <summary>The non-secret leading characters kept in clear for display/audit.</summary>
    public static string DisplayPrefix(string rawKey) =>
        rawKey.Length <= 12 ? rawKey : rawKey[..12];

    /// <summary>
    /// Mints a new key from a CSPRNG. Returns the raw secret (show it once, then discard) plus the
    /// values to persist (the visible prefix and the hash).
    /// </summary>
    public static (string RawKey, string Prefix, string Hash) Generate()
    {
        // 256 bits of entropy, base64url, no padding → URL/header-safe.
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        var rawKey = KeyPrefix + secret;
        return (rawKey, DisplayPrefix(rawKey), Hash(rawKey));
    }
}
