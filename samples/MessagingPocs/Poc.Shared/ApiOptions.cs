namespace Poc.Shared;

/// <summary>Where the API is and how to authenticate to it. Bound from the "Api" section of each app's appsettings.json.</summary>
/// <remarks>
/// ✅ DO use the same credentials for everything the app does with the API (HTTP, and the SignalR hub in POC 3).
/// ❌ DON'T ship a real API key in appsettings.json. This is the seeded development key. A real deployment reads it
///    from a protected per-machine store (DPAPI, Windows Credential Manager) or signs users in.
/// </remarks>
public sealed class ApiOptions
{
    public const string SectionName = "Api";

    public string BaseUrl { get; set; } = "http://localhost:5235/";

    public string ApiKey { get; set; } = "";

    /// <summary>Recorded as the actor in the API's audit trail (the X-Actor development header).</summary>
    public string Actor { get; set; } = "poc";
}
