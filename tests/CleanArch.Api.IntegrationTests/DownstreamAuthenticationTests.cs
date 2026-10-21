using System.Net;
using System.Text;
using System.Text.Json;
using CleanArch.Api.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace CleanArch.Api.IntegrationTests;

/// <summary>
/// Every way this API can authenticate to a downstream, side by side — the worked example behind
/// <c>docs/downstream-authentication.md</c>. One client per scenario, each wearing the REAL handler; only
/// the IdP token endpoint and the downstream services are faked (as <see cref="HttpMessageHandler"/>s), so
/// what is asserted is exactly what would cross the wire.
/// <list type="table">
/// <item><term>billing</term><description>On-Behalf-Of — the user, re-scoped to billing's audience</description></item>
/// <item><term>reports</term><description>Forwarded bearer — the user's own token, unchanged</description></item>
/// <item><term>inventory</term><description>Client credentials — this API, no user</description></item>
/// <item><term>legacy</term><description>Basic — this API's service account</description></item>
/// <item><term>vendor</term><description>API key — this API's key, in the vendor's own header</description></item>
/// </list>
/// </summary>
public sealed class DownstreamAuthenticationTests
{
    private const string TokenEndpoint = "https://idp.example/oauth2/token";
    private const string UserToken = "user-token-for-students-api";

    private static readonly Dictionary<string, string?> Settings = new()
    {
        // This API's one identity at the provider — shared by On-Behalf-Of and client credentials.
        ["OnBehalfOf:TokenEndpoint"] = TokenEndpoint,
        ["OnBehalfOf:ClientId"] = "students-api",
        ["OnBehalfOf:ClientSecret"] = "super-secret",
        ["OnBehalfOf:Downstreams:billing:Audience"] = "api://billing",
        ["OnBehalfOf:Downstreams:inventory:Audience"] = "api://inventory",
        ["OnBehalfOf:Downstreams:inventory:Scope"] = "stock.read",

        // Service-account credentials for the downstreams that take no OAuth tokens.
        ["DownstreamCredentials:legacy:Username"] = "svc-cleanarch",
        ["DownstreamCredentials:legacy:Password"] = "legacy-password",
        ["DownstreamCredentials:vendor:ApiKey"] = "vendor-key-123",
        ["DownstreamCredentials:vendor:ApiKeyHeader"] = "X-Vendor-Key",
    };

    [Fact]
    public async Task One_request_reaches_every_downstream_with_the_credential_that_downstream_accepts()
    {
        var idp = new FakeIdp();
        var downstreams = new FakeDownstreams();
        await using var provider = BuildPipeline(idp, downstreams, callerToken: UserToken);
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        foreach (var name in new[] { "billing", "reports", "inventory", "legacy", "vendor" })
        {
            var response = await factory.CreateClient(name).GetAsync("/ping");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // billing: the USER, but in a token minted for billing's audience — not the token we were handed.
        Assert.Equal($"Bearer obo|api://billing|{UserToken}", downstreams.Seen["billing"].Authorization);

        // reports: the user's own token, byte for byte. No IdP involved.
        Assert.Equal($"Bearer {UserToken}", downstreams.Seen["reports"].Authorization);

        // inventory: THIS API's token for inventory's audience — the user appears nowhere in it.
        Assert.Equal("Bearer cc|api://inventory", downstreams.Seen["inventory"].Authorization);

        // legacy: the service account over Basic.
        Assert.Equal(
            "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("svc-cleanarch:legacy-password")),
            downstreams.Seen["legacy"].Authorization);

        // vendor: the key, in the header the vendor named — and no Authorization header at all.
        Assert.Equal("vendor-key-123", downstreams.Seen["vendor"].Headers["X-Vendor-Key"]);
        Assert.Null(downstreams.Seen["vendor"].Authorization);

        // Exactly two IdP round-trips: one exchange (billing) and one client-credentials grant (inventory).
        Assert.Equal(["token-exchange", "client_credentials"], idp.Grants.Select(ShortGrant));
    }

    [Fact]
    public async Task Forwarding_refuses_a_caller_who_has_no_bearer_token()
    {
        var downstreams = new FakeDownstreams();
        // An API-key or Basic caller: authenticated, but with no bearer token to pass on.
        await using var provider = BuildPipeline(new FakeIdp(), downstreams, callerToken: null);

        var ex = await Assert.ThrowsAsync<CallerTokenRequiredException>(
            () => provider.GetRequiredService<IHttpClientFactory>().CreateClient("reports").GetAsync("/ping"));

        // Refused before anything left the process — never quietly sent unauthenticated, nor as the service.
        Assert.Equal("reports", ex.DownstreamName);
        Assert.Empty(downstreams.Seen);
    }

    [Fact]
    public async Task Service_credentials_do_not_need_a_caller_at_all()
    {
        var idp = new FakeIdp();
        var downstreams = new FakeDownstreams();
        // No request in flight — background work, a hosted service, a message consumer.
        await using var provider = BuildPipeline(idp, downstreams, callerToken: null, inRequest: false);
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        foreach (var name in new[] { "inventory", "legacy", "vendor" })
        {
            Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient(name).GetAsync("/ping")).StatusCode);
        }

        Assert.Equal("Bearer cc|api://inventory", downstreams.Seen["inventory"].Authorization);
        Assert.StartsWith("Basic ", downstreams.Seen["legacy"].Authorization, StringComparison.Ordinal);
        Assert.Equal("vendor-key-123", downstreams.Seen["vendor"].Headers["X-Vendor-Key"]);
    }

    [Fact]
    public async Task Client_credentials_ask_for_the_downstreams_audience_and_reuse_the_token()
    {
        var idp = new FakeIdp();
        await using var provider = BuildPipeline(idp, new FakeDownstreams(), callerToken: null, inRequest: false);
        var inventory = provider.GetRequiredService<IHttpClientFactory>().CreateClient("inventory");

        await inventory.GetAsync("/ping");
        await inventory.GetAsync("/ping");

        // One grant serves every caller until near expiry: the token is the service's, not any user's.
        Assert.Single(idp.Grants);
        var form = idp.Grants[0];
        Assert.Equal("client_credentials", form["grant_type"]);
        Assert.Equal("api://inventory", form["audience"]);
        Assert.Equal("stock.read", form["scope"]);
        Assert.False(form.ContainsKey("subject_token"));
        Assert.Equal(("students-api", "super-secret"), idp.LastClientCredentials);
    }

    [Fact]
    public async Task A_rejected_client_credentials_grant_is_this_APIs_problem_not_the_callers()
    {
        var idp = new FakeIdp { StatusToReturn = HttpStatusCode.Unauthorized };
        await using var provider = BuildPipeline(idp, new FakeDownstreams(), callerToken: UserToken);

        var ex = await Assert.ThrowsAsync<TokenExchangeException>(
            () => provider.GetRequiredService<IHttpClientFactory>().CreateClient("inventory").GetAsync("/ping"));

        // A wrong secret or ungranted scope is configuration — the host answers 502, not 401, because
        // nothing the caller does can fix it.
        Assert.Equal(TokenExchangeFailure.ClientRejected, ex.Failure);
        Assert.Equal("inventory", ex.DownstreamName);
    }

    [Theory]
    [InlineData("DownstreamCredentials:legacy:Password", "DownstreamCredentials:legacy")]
    [InlineData("DownstreamCredentials:vendor:ApiKey", "DownstreamCredentials:vendor")]
    [InlineData("OnBehalfOf:Downstreams:inventory:Audience", "OnBehalfOf:Downstreams:inventory")]
    public async Task Refuses_to_start_when_a_downstream_credential_is_missing(string unset, string named)
    {
        // The secret that was never set in this environment: a boot failure that names it, instead of a
        // 401 from the far end on the first real call.
        var settings = new Dictionary<string, string?>(Settings) { [unset] = null };
        if (unset.EndsWith(":Audience", StringComparison.Ordinal))
        {
            settings["OnBehalfOf:Downstreams:inventory:Scope"] = null;
        }

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddLogging();
        builder.Services.AddHybridCache();
        builder.Services.AddOnBehalfOf(builder.Configuration);
        RegisterDownstreams(builder.Services, new FakeIdp(), new FakeDownstreams());
        using var host = builder.Build();

        var failure = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(named, failure.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider BuildPipeline(
        FakeIdp idp, FakeDownstreams downstreams, string? callerToken, bool inRequest = true)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(Settings).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddHybridCache();
        services.AddOnBehalfOf(configuration);

        // Stand in for the JWT handler, which keeps a bearer caller's token on the request (SaveToken).
        services.AddSingleton<IAuthenticationService>(new StubAuthenticationService(callerToken));

        RegisterDownstreams(services, idp, downstreams);

        var provider = services.BuildServiceProvider();
        if (inRequest)
        {
            provider.GetRequiredService<IHttpContextAccessor>().HttpContext =
                new DefaultHttpContext { RequestServices = provider };
        }

        return provider;
    }

    /// <summary>The five registrations exactly as Program.cs would write them, plus fakes for the network.</summary>
    private static void RegisterDownstreams(IServiceCollection services, FakeIdp idp, FakeDownstreams downstreams)
    {
        services.AddHttpClient(TokenExchangeClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => idp);

        services.AddHttpClient("billing", Base("billing")).AddOnBehalfOf()
            .ConfigurePrimaryHttpMessageHandler(() => downstreams);
        services.AddHttpClient("reports", Base("reports")).AddForwardedBearer()
            .ConfigurePrimaryHttpMessageHandler(() => downstreams);
        services.AddHttpClient("inventory", Base("inventory")).AddClientCredentials()
            .ConfigurePrimaryHttpMessageHandler(() => downstreams);
        services.AddHttpClient("legacy", Base("legacy")).AddBasicCredentials()
            .ConfigurePrimaryHttpMessageHandler(() => downstreams);
        services.AddHttpClient("vendor", Base("vendor")).AddApiKeyCredentials()
            .ConfigurePrimaryHttpMessageHandler(() => downstreams);

        static Action<HttpClient> Base(string name) =>
            client => client.BaseAddress = new Uri($"https://{name}.example/");
    }

    private static string ShortGrant(Dictionary<string, string> form) =>
        form["grant_type"].Split(':')[^1];

    /// <summary>
    /// The IdP token endpoint. Tokens are opaque strings that spell out what they were minted for, so a
    /// downstream assertion reads as the claim it stands for: <c>obo|audience|subject</c> or <c>cc|audience</c>.
    /// </summary>
    private sealed class FakeIdp : HttpMessageHandler
    {
        public List<Dictionary<string, string>> Grants { get; } = [];
        public (string, string) LastClientCredentials { get; private set; }
        public HttpStatusCode StatusToReturn { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var form = QueryHelpers.ParseQuery(body).ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
            Grants.Add(form);

            var basic = Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization!.Parameter!))
                .Split(':', 2);
            LastClientCredentials = (basic[0], basic[1]);

            if (StatusToReturn != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(StatusToReturn)
                {
                    Content = new StringContent("""{"error":"invalid_client"}""", Encoding.UTF8, "application/json"),
                };
            }

            var token = form["grant_type"] == "client_credentials"
                ? $"cc|{form["audience"]}"
                : $"obo|{form["audience"]}|{form["subject_token"]}";
            var json = JsonSerializer.Serialize(new { access_token = token, token_type = "Bearer", expires_in = 3600 });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>Every downstream: records what credential arrived, keyed by the host it was sent to.</summary>
    private sealed class FakeDownstreams : HttpMessageHandler
    {
        public Dictionary<string, SeenRequest> Seen { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var name = request.RequestUri!.Host.Split('.')[0];
            Seen[name] = new SeenRequest(
                request.Headers.Authorization?.ToString(),
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed record SeenRequest(string? Authorization, Dictionary<string, string> Headers);

    /// <summary>Returns a ticket carrying the saved access token, exactly as the JWT handler would.</summary>
    private sealed class StubAuthenticationService : IAuthenticationService
    {
        private readonly string? _accessToken;
        public StubAuthenticationService(string? accessToken) => _accessToken = accessToken;

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        {
            if (_accessToken is null)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principal = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity("Bearer"));
            var props = new AuthenticationProperties();
            props.StoreTokens([new AuthenticationToken { Name = "access_token", Value = _accessToken }]);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, props, scheme ?? "Bearer")));
        }

        public Task ChallengeAsync(HttpContext c, string? s, AuthenticationProperties? p) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext c, string? s, AuthenticationProperties? p) => Task.CompletedTask;
        public Task SignInAsync(HttpContext c, string? s, System.Security.Claims.ClaimsPrincipal pr, AuthenticationProperties? p) => Task.CompletedTask;
        public Task SignOutAsync(HttpContext c, string? s, AuthenticationProperties? p) => Task.CompletedTask;
    }
}
