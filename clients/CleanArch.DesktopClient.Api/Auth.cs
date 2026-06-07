using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace CleanArch.DesktopClient.Api;

public interface ITokenStore
{
    bool IsSignedIn { get; }
    string? Actor { get; }
    string? AccessToken { get; }
    Task SignInAsync(string actor, IReadOnlyList<string> roles, CancellationToken ct = default);
    void SignOut();
}

/// <summary>
/// POC token store: obtains a JWT from the API's dev token endpoint. Swap for an OIDC flow
/// (e.g. IdentityModel.OidcClient with Authorization Code + PKCE) for production.
/// </summary>
public sealed class DevTokenStore : ITokenStore
{
    private readonly HttpClient _http;
    public DevTokenStore(HttpClient http) => _http = http;

    public bool IsSignedIn => AccessToken is not null;
    public string? Actor { get; private set; }
    public string? AccessToken { get; private set; }

    public async Task SignInAsync(string actor, IReadOnlyList<string> roles, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("dev/token", new { actor, roles }, ct);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        AccessToken = payload?.Token ?? throw new InvalidOperationException("Token endpoint returned no token.");
        Actor = actor;
    }

    public void SignOut()
    {
        AccessToken = null;
        Actor = null;
    }

    private sealed record TokenResponse(string Token);
}

/// <summary>Attaches the bearer token (and a correlation id) to every outgoing API request.</summary>
internal sealed class AuthHeaderHandler : DelegatingHandler
{
    private readonly ITokenStore _tokens;
    public AuthHeaderHandler(ITokenStore tokens) => _tokens = tokens;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_tokens.AccessToken is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (!request.Headers.Contains("X-Correlation-ID"))
        {
            request.Headers.TryAddWithoutValidation("X-Correlation-ID", Guid.NewGuid().ToString());
        }

        return await base.SendAsync(request, cancellationToken);
    }
}

/// <summary>Builds the configured API clients (single long-lived HttpClient with the auth handler).</summary>
public static class ApiClientFactory
{
    public static (ITokenStore tokens, IStudentsApiClient students, ILibraryApiClient library) Create(string baseUrl)
    {
        var baseUri = new Uri(baseUrl);

        // Plain client for the (open) token endpoint.
        var tokens = new DevTokenStore(new HttpClient { BaseAddress = baseUri });

        // Authenticated client for the API; the handler reads the token from the store on each call.
        var authed = new HttpClient(new AuthHeaderHandler(tokens) { InnerHandler = new HttpClientHandler() })
        {
            BaseAddress = baseUri,
        };

        return (tokens, new StudentsApiClient(authed), new LibraryApiClient(authed));
    }
}
