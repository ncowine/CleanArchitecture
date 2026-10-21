namespace CleanArch.Api.Authentication;

/// <summary>
/// Fixed service-account credentials for a downstream that does not take OAuth tokens — HTTP Basic or an
/// API key. Bound as NAMED options from <c>DownstreamCredentials:{name}</c>, where <c>{name}</c> is the
/// <c>HttpClient</c>'s name, and validated at startup for whichever method that client uses:
/// <code>
/// "DownstreamCredentials": {
///   "legacy": { "Username": "svc-cleanarch" },          // Password from env / Key Vault / user-secrets
///   "vendor": { "ApiKeyHeader": "X-Vendor-Key" }        // ApiKey from env / Key Vault / user-secrets
/// }
/// </code>
/// These are THIS API's credentials, never the caller's: the downstream sees the service, not the user.
/// </summary>
internal sealed class DownstreamCredentialOptions
{
    public const string SectionName = "DownstreamCredentials";

    /// <summary>The section a single named downstream binds from.</summary>
    public static string SectionFor(string downstreamName) => $"{SectionName}:{downstreamName}";

    /// <summary>Basic: the service account's user name.</summary>
    public string? Username { get; set; }

    /// <summary>
    /// Basic: the service account's password. SECRET — supply it from env
    /// (<c>DownstreamCredentials__{name}__Password</c>), a secrets manager, or user-secrets; never appsettings.json.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>API key: the key value. SECRET — same rules as <see cref="Password"/>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>API key: the header that carries it. Most APIs use the default; some name their own.</summary>
    public string ApiKeyHeader { get; set; } = "X-Api-Key";

    public bool HasBasic => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrEmpty(Password);

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ApiKeyHeader);
}
