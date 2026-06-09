namespace CleanArch.Api.Authentication;

/// <summary>
/// Canonical authentication scheme names and the API-key header name — the single source of truth for
/// these strings. Public so the host (e.g. Swagger security definitions) can reference them WITHOUT
/// depending on the internal handler types. The handlers alias these values.
/// </summary>
public static class AuthenticationSchemes
{
    public const string ApiKey = "ApiKey";
    public const string Basic = "Basic";
    public const string Bearer = "Bearer";
    public const string ApiKeyHeaderName = "X-Api-Key";
}
