namespace CleanArch.DesktopClient.Api;

public interface IAuthSession
{
    bool IsSignedIn { get; }
    string? Actor { get; }
    Task SignInAsync(string actor, IReadOnlyList<string> roles, CancellationToken ct = default);
    void SignOut();
}

/// <summary>
/// POC sign-in. The API authorizes this client by a service API key (the <c>X-Api-Key</c> scheme),
/// so "signing in" doesn't fetch a token — it just records who the operator is, which travels as the
/// <c>X-Actor</c> header for audit. Swap for an interactive OIDC flow (e.g. IdentityModel.OidcClient,
/// Authorization Code + PKCE) when the client needs real per-user identity.
/// </summary>
public sealed class ApiKeyAuthSession : IAuthSession
{
    public bool IsSignedIn { get; private set; }
    public string? Actor { get; private set; }

    public Task SignInAsync(string actor, IReadOnlyList<string> roles, CancellationToken ct = default)
    {
        Actor = actor;
        IsSignedIn = true;
        return Task.CompletedTask;
    }

    public void SignOut()
    {
        IsSignedIn = false;
        Actor = null;
    }
}

/// <summary>Attaches the service API key, the operator (for audit), and a correlation id to every request.</summary>
internal sealed class ApiKeyAuthHandler : DelegatingHandler
{
    private const string ApiKeyHeader = "X-Api-Key";
    private const string ActorHeader = "X-Actor";
    private const string CorrelationHeader = "X-Correlation-ID";

    private readonly IAuthSession _session;
    private readonly string _apiKey;

    public ApiKeyAuthHandler(IAuthSession session, string apiKey)
    {
        _session = session;
        _apiKey = apiKey;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains(ApiKeyHeader))
        {
            request.Headers.TryAddWithoutValidation(ApiKeyHeader, _apiKey);
        }

        if (_session.Actor is { } actor && !request.Headers.Contains(ActorHeader))
        {
            request.Headers.TryAddWithoutValidation(ActorHeader, actor);
        }

        if (!request.Headers.Contains(CorrelationHeader))
        {
            request.Headers.TryAddWithoutValidation(CorrelationHeader, Guid.NewGuid().ToString());
        }

        return await base.SendAsync(request, cancellationToken);
    }
}

/// <summary>Builds the configured API clients (single long-lived HttpClient with the API-key handler).</summary>
public static class ApiClientFactory
{
    // Cap each call so an unreachable host fails in seconds (with a friendly timeout message) rather
    // than hanging on the default 100s HttpClient timeout.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public static (
        IAuthSession session,
        IStudentsApiClient students,
        ILibraryApiClient library,
        IBillingApiClient billing,
        IAcademicsApiClient academics) Create(string baseUrl, string apiKey)
    {
        var baseUri = new Uri(baseUrl);
        var session = new ApiKeyAuthSession();

        var authed = new HttpClient(new ApiKeyAuthHandler(session, apiKey) { InnerHandler = new HttpClientHandler() })
        {
            BaseAddress = baseUri,
            Timeout = RequestTimeout,
        };

        return (
            session,
            new StudentsApiClient(authed),
            new LibraryApiClient(authed),
            new BillingApiClient(authed),
            new AcademicsApiClient(authed));
    }
}
