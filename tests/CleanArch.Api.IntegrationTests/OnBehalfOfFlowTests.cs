using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArch.Api.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace CleanArch.Api.IntegrationTests;

/// <summary>
/// End-to-end demonstration of the OAuth2 On-Behalf-Of flow against the REAL production pipeline
/// (<see cref="OnBehalfOfHandler"/> → <c>CachingTokenExchangeClient</c> → <c>TokenExchangeClient</c>).
/// The only fakes are the two things that would be across the network in production: the IdP token
/// endpoint and the downstream API. Both are stubbed as <see cref="HttpMessageHandler"/>s so no live
/// IdP or service is needed — yet every line of our own code runs for real, including the RFC 8693
/// request the exchange client builds and the Bearer token the handler attaches.
/// </summary>
public sealed class OnBehalfOfFlowTests
{
    private const string TokenEndpoint = "https://idp.example/oauth2/token";
    private const string ApiClientId = "students-api";
    private const string ApiClientSecret = "super-secret";
    private const string DownstreamAudience = "billing-api";
    private const string DownstreamScope = "billing.read";
    private const string UserSubject = "alice@university.edu";

    private readonly ITestOutputHelper _output;

    public OnBehalfOfFlowTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Calls_downstream_as_the_authenticated_user_not_as_the_service()
    {
        var trace = new List<string>();
        void Trace(string line) { trace.Add(line); _output.WriteLine(line); }

        // ── The incoming caller's token. In production the JWT handler validates this and stashes it
        //    (SaveToken=true); here we mint a realistic-looking one and hand it to a stub auth service.
        var userToken = FakeJwt.Mint(("sub", UserSubject), ("aud", ApiClientId), ("name", "Alice"));
        Trace($"1. User calls our API with their token   sub={UserSubject}, aud={ApiClientId}");

        var idp = new FakeIdpHandler(Trace);
        var downstream = new FakeDownstreamHandler(Trace);

        await using var provider = BuildPipeline(userToken, idp, downstream);

        // Simulate being inside an authenticated request: the handler reads the token off HttpContext.
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { RequestServices = provider };

        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("Downstream");

        // ── The actual outbound call. Our OnBehalfOfHandler intercepts it, exchanges the user token,
        //    and attaches the downstream-scoped token — all before the request leaves the process.
        Trace("2. Our API calls the downstream API through the OBO-equipped HttpClient");
        var response = await http.GetAsync("/invoices");
        var seen = await response.Content.ReadFromJsonAsync<DownstreamView>();

        Trace($"5. Downstream authorized the request as   sub={seen!.SeenSub}, aud={seen.SeenAud}");

        // ── Assertions: the downstream saw the ORIGINAL USER, scoped to ITS OWN audience.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(UserSubject, seen.SeenSub);          // same user — true impersonation
        Assert.Equal(DownstreamAudience, seen.SeenAud);   // re-scoped to the downstream API
        Assert.NotEqual(ApiClientId, seen.SeenAud);       // NOT the token we were handed

        // The exchange client built a spec-compliant RFC 8693 request, authenticated as our confidential client.
        Assert.Equal("urn:ietf:params:oauth:grant-type:token-exchange", idp.LastForm["grant_type"]);
        Assert.Equal(userToken, idp.LastForm["subject_token"]);
        Assert.Equal(DownstreamAudience, idp.LastForm["audience"]);
        Assert.Equal((ApiClientId, ApiClientSecret), idp.LastClientCredentials);

        // ── Caching decorator: a second call for the same user does NOT hit the IdP again.
        Trace("6. Second call for the same user...");
        await http.GetAsync("/invoices");
        Assert.Equal(1, idp.ExchangeCount);
        Trace($"   IdP exchanges performed: {idp.ExchangeCount} (second call served from cache)");

        WriteTraceFile(trace);
    }

    [Fact]
    public async Task Fails_loudly_when_the_caller_has_no_user_token()
    {
        var idp = new FakeIdpHandler(_ => { });
        var downstream = new FakeDownstreamHandler(_ => { });
        await using var provider = BuildPipeline(userToken: null, idp, downstream);

        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { RequestServices = provider };

        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("Downstream");

        // A service caller (API key / Basic) has no user token to exchange — OBO must refuse, not silently
        // fall back to a service identity. That refusal is the whole point of the design, and it surfaces
        // as a SubjectRejected exception the host maps to 401 (never a 500, never a service-identity call).
        var ex = await Assert.ThrowsAsync<TokenExchangeException>(() => http.GetAsync("/invoices"));
        Assert.Equal(TokenExchangeFailure.SubjectRejected, ex.Failure);
        Assert.Equal(0, idp.ExchangeCount);
    }

    [Fact]
    public async Task Re_exchanges_when_the_cached_token_is_near_expiry()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));
        var userToken = FakeJwt.Mint(("sub", UserSubject), ("aud", ApiClientId));
        var idp = new FakeIdpHandler(_ => { }) { TokenLifetimeSeconds = 120 }; // skew is 60s
        var downstream = new FakeDownstreamHandler(_ => { });

        await using var provider = BuildPipeline(userToken, idp, downstream, clock);
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext =
            new DefaultHttpContext { RequestServices = provider };
        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("Downstream");

        await http.GetAsync("/invoices");
        Assert.Equal(1, idp.ExchangeCount);

        // Still inside the safe window — served from cache, no new exchange.
        clock.Advance(TimeSpan.FromSeconds(30));
        await http.GetAsync("/invoices");
        Assert.Equal(1, idp.ExchangeCount);

        // Now within the 60s skew of the 120s token's expiry — must re-exchange rather than serve a token
        // that could expire in flight.
        clock.Advance(TimeSpan.FromSeconds(40)); // t = 70s, expiry at 120s, skew 60s → stale
        await http.GetAsync("/invoices");
        Assert.Equal(2, idp.ExchangeCount);
    }

    [Fact]
    public async Task Maps_a_rejected_subject_token_to_a_SubjectRejected_failure()
    {
        var userToken = FakeJwt.Mint(("sub", UserSubject), ("aud", ApiClientId));
        var idp = new FakeIdpHandler(_ => { }) { StatusToReturn = HttpStatusCode.BadRequest };
        var downstream = new FakeDownstreamHandler(_ => { });

        await using var provider = BuildPipeline(userToken, idp, downstream);
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext =
            new DefaultHttpContext { RequestServices = provider };
        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("Downstream");

        // The provider returned 400 (invalid_grant) — the caller's token is the problem (→ 401), and the
        // downstream is never called with a bad token.
        var ex = await Assert.ThrowsAsync<TokenExchangeException>(() => http.GetAsync("/invoices"));
        Assert.Equal(TokenExchangeFailure.SubjectRejected, ex.Failure);
    }

    [Fact]
    public async Task Authenticates_with_a_signed_assertion_when_using_private_key_jwt()
    {
        // The API's keypair: the private key signs the assertion; the provider would hold the public key.
        using var rsa = RSA.Create(2048);
        var privatePem = rsa.ExportPkcs8PrivateKeyPem();

        var userToken = FakeJwt.Mint(("sub", UserSubject), ("aud", ApiClientId));
        var idp = new FakeIdpHandler(_ => { });
        var downstream = new FakeDownstreamHandler(_ => { });

        await using var provider = BuildPipeline(userToken, idp, downstream, extraConfig: new Dictionary<string, string?>
        {
            ["OnBehalfOf:ClientAuthentication"] = "PrivateKeyJwt",
            ["OnBehalfOf:SigningKeyPem"] = privatePem,
            ["OnBehalfOf:SigningKeyId"] = "test-key-1",
            ["OnBehalfOf:ClientSecret"] = "",   // prove the secret is genuinely unused
        });
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext =
            new DefaultHttpContext { RequestServices = provider };
        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("Downstream");

        var response = await http.GetAsync("/invoices");
        var seen = await response.Content.ReadFromJsonAsync<DownstreamView>();

        // The flow still works end-to-end, as the authenticated user.
        Assert.Equal(UserSubject, seen!.SeenSub);
        Assert.Equal(DownstreamAudience, seen.SeenAud);

        // But NO secret / Basic header crossed the wire — the proof was a signed assertion instead.
        Assert.Null(idp.LastAuthorizationHeader);
        Assert.NotNull(idp.LastClientAssertion);
        Assert.Equal("urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
            idp.LastForm["client_assertion_type"]);

        // The assertion is a real RS256 JWT the provider verifies with the PUBLIC key, scoped to itself
        // (aud = token endpoint) and issued by this client — exactly what RFC 7523 requires.
        Assert.True(VerifyRs256(idp.LastClientAssertion!, rsa));
        Assert.Equal(ApiClientId, ReadClaim(idp.LastClientAssertion!, "iss"));
        Assert.Equal(ApiClientId, ReadClaim(idp.LastClientAssertion!, "sub"));
        Assert.Equal(TokenEndpoint, ReadClaim(idp.LastClientAssertion!, "aud"));
    }

    private static bool VerifyRs256(string jwt, RSA publicKey)
    {
        var parts = jwt.Split('.');
        var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        var signature = FakeJwt.UnB64(parts[2]);
        return publicKey.VerifyData(
            signingInput, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    private static string ReadClaim(string jwt, string name)
    {
        using var doc = JsonDocument.Parse(FakeJwt.UnB64(jwt.Split('.')[1]));
        var element = doc.RootElement.GetProperty(name);
        // A single audience may serialize as a string or a one-element array.
        return element.ValueKind == JsonValueKind.Array ? element[0].GetString()! : element.GetString()!;
    }

    private static ServiceProvider BuildPipeline(
        string? userToken, FakeIdpHandler idp, FakeDownstreamHandler downstream, TimeProvider? clock = null,
        IDictionary<string, string?>? extraConfig = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["OnBehalfOf:TokenEndpoint"] = TokenEndpoint,
            ["OnBehalfOf:ClientId"] = ApiClientId,
            ["OnBehalfOf:ClientSecret"] = ApiClientSecret,
            ["OnBehalfOf:Audience"] = DownstreamAudience,
            ["OnBehalfOf:Scope"] = DownstreamScope,
        };
        if (extraConfig is not null)
        {
            foreach (var kv in extraConfig)
            {
                settings[kv.Key] = kv.Value;
            }
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddHybridCache();

        // Controllable clock so expiry is deterministic (AddOnBehalfOf TryAdds TimeProvider.System, so this wins).
        if (clock is not null)
        {
            services.AddSingleton(clock);
        }

        // The real registrations under test.
        services.AddOnBehalfOf(configuration);

        // Stand in for the JWT handler that would have validated + stashed the caller's token.
        services.AddSingleton<IAuthenticationService>(new StubAuthenticationService(userToken));

        // Fake the IdP token endpoint (the network hop the exchange client makes).
        services.AddHttpClient(TokenExchangeClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => idp);

        // The downstream API client, wearing the real OBO handler, with the downstream itself faked.
        services.AddHttpClient("Downstream", c => c.BaseAddress = new Uri("https://billing.example/"))
            .AddHttpMessageHandler<OnBehalfOfHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => downstream);

        return services.BuildServiceProvider();
    }

    private static void WriteTraceFile(List<string> trace)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "obo-e2e-trace.txt");
        File.WriteAllText(path, string.Join(Environment.NewLine, trace) + Environment.NewLine);
    }

    private sealed record DownstreamView(string SeenSub, string SeenAud);

    /// <summary>The IdP's token endpoint: validates the RFC 8693 request and mints a re-scoped token.</summary>
    private sealed class FakeIdpHandler : HttpMessageHandler
    {
        private readonly Action<string> _trace;
        public int ExchangeCount { get; private set; }
        public Dictionary<string, string> LastForm { get; private set; } = new();
        public (string, string) LastClientCredentials { get; private set; }
        public AuthenticationHeaderValue? LastAuthorizationHeader { get; private set; }
        public string? LastClientAssertion { get; private set; }

        /// <summary>Lifetime the IdP stamps on the minted token (drives cache expiry).</summary>
        public int TokenLifetimeSeconds { get; set; } = 3600;

        /// <summary>Set non-OK to simulate the provider rejecting the subject token / erroring.</summary>
        public HttpStatusCode StatusToReturn { get; set; } = HttpStatusCode.OK;

        public FakeIdpHandler(Action<string> trace) => _trace = trace;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ExchangeCount++;
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            LastForm = QueryHelpers.ParseQuery(body).ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

            // Capture whichever client-auth proof arrived: Basic header (client_secret) or assertion form
            // field (private_key_jwt). Only one should be present for a given configuration.
            LastAuthorizationHeader = request.Headers.Authorization;
            if (request.Headers.Authorization is { } auth)
            {
                LastClientCredentials = DecodeBasic(auth);
            }
            LastClientAssertion = LastForm.GetValueOrDefault("client_assertion");

            if (StatusToReturn != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(StatusToReturn)
                {
                    Content = new StringContent(
                        """{"error":"invalid_grant","error_description":"subject token rejected"}""",
                        Encoding.UTF8, "application/json"),
                };
            }

            var subjectClaims = FakeJwt.Read(LastForm["subject_token"]);
            var user = subjectClaims["sub"];
            _trace($"3. IdP token exchange (RFC 8693): client '{LastClientCredentials.Item1}' swaps the user " +
                   $"token for one scoped to '{LastForm["audience"]}' — subject stays '{user}'");

            // Mint the downstream token: SAME subject, NEW audience/scope.
            var downstreamToken = FakeJwt.Mint(
                ("sub", user), ("aud", LastForm["audience"]), ("scope", LastForm.GetValueOrDefault("scope", "")));

            var json = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["access_token"] = downstreamToken,
                ["token_type"] = "Bearer",
                ["expires_in"] = TokenLifetimeSeconds,
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }

        private static (string, string) DecodeBasic(AuthenticationHeaderValue header)
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter!));
            var parts = raw.Split(':', 2);
            return (parts[0], parts[1]);
        }
    }

    /// <summary>The downstream API: reads the Bearer token and reports whom it authorized.</summary>
    private sealed class FakeDownstreamHandler : HttpMessageHandler
    {
        private readonly Action<string> _trace;
        public FakeDownstreamHandler(Action<string> trace) => _trace = trace;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var token = request.Headers.Authorization!.Parameter!;
            var claims = FakeJwt.Read(token);
            _trace($"4. Downstream receives 'Authorization: Bearer ...' and validates the token's claims");

            var json = JsonSerializer.Serialize(new DownstreamView(claims["sub"], claims["aud"]));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>A hand-wound clock so token-expiry behaviour is deterministic.</summary>
    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset _now;
        public MutableClock(DateTimeOffset start) => _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

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

    /// <summary>
    /// Minimal JWT-shaped token: <c>header.payload.signature</c> with a base64url JSON payload. Our
    /// production code treats tokens as opaque strings, so an unsigned stand-in faithfully represents
    /// what flows through it — only the fake IdP/downstream read the claims, as real ones would.
    /// </summary>
    private static class FakeJwt
    {
        public static string Mint(params (string Key, string Value)[] claims)
        {
            var header = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "none", typ = "JWT" }));
            var payload = B64(JsonSerializer.SerializeToUtf8Bytes(
                claims.ToDictionary(c => c.Key, c => c.Value)));
            return $"{header}.{payload}.";
        }

        public static Dictionary<string, string> Read(string token)
        {
            var payload = token.Split('.')[1];
            return JsonSerializer.Deserialize<Dictionary<string, string>>(UnB64(payload))!;
        }

        private static string B64(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static byte[] UnB64(string value)
        {
            var s = value.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
        }
    }
}
