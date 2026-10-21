using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Downstream.Sample;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

// One process playing two roles for the live downstream-authentication demo (README.md):
//   1. the identity provider the CleanArch API trusts (DevIdentityProvider), and
//   2. five downstream APIs, each accepting exactly ONE kind of credential and reporting who it authorized.
var builder = WebApplication.CreateBuilder(args);

var idp = new DevIdentityProvider(builder.Configuration.GetSection("DevIdp"));
var downstreams = builder.Configuration.GetSection("Downstreams");

// The token-accepting downstreams: one bearer scheme each, accepting ONE audience. A token minted for any
// other audience is refused with 401 and a WWW-Authenticate header saying why — which is exactly what
// happens when a token is forwarded to a service it was not issued for.
string[] tokenDownstreams = ["billing", "inventory", "reports"];
var authentication = builder.Services.AddAuthentication();
foreach (var name in tokenDownstreams)
{
    authentication.AddJwtBearer(name, options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = idp.Issuer,
            ValidAudience = downstreams[$"{name}:Audience"],
            IssuerSigningKey = idp.SigningKey,
            NameClaimType = "sub",
        };
    });
}

builder.Services.AddAuthorization(options =>
{
    foreach (var name in tokenDownstreams)
    {
        options.AddPolicy(name, policy => policy.AddAuthenticationSchemes(name).RequireAuthenticatedUser());
    }
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

idp.Map(app);

// ── Downstreams that take OAuth bearer tokens ─────────────────────────────────────────────────────────
app.MapGet("/billing/whoami", (ClaimsPrincipal user) => FromToken("billing", user)).RequireAuthorization("billing");
app.MapGet("/inventory/whoami", (ClaimsPrincipal user) => FromToken("inventory", user)).RequireAuthorization("inventory");
app.MapGet("/reports/whoami", (ClaimsPrincipal user) => FromToken("reports", user)).RequireAuthorization("reports");

// ── Downstreams that take legacy credentials ──────────────────────────────────────────────────────────
app.MapGet("/legacy/whoami", (HttpRequest request) =>
{
    var header = request.Headers.Authorization.ToString();
    if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
    {
        return BasicChallenge();
    }

    string[] parts;
    try
    {
        parts = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..].Trim())).Split(':', 2);
    }
    catch (FormatException)
    {
        return BasicChallenge();
    }

    var valid = parts.Length == 2
        && DevIdentityProvider.FixedTimeEquals(parts[0], downstreams["legacy:Username"]!)
        && DevIdentityProvider.FixedTimeEquals(parts[1], downstreams["legacy:Password"]!);
    return valid
        ? Results.Ok(new WhoAmI("legacy", "HTTP Basic (service account)", parts[0], null, null, null))
        : BasicChallenge();
});

app.MapGet("/vendor/whoami", (HttpRequest request) =>
{
    var headerName = downstreams["vendor:ApiKeyHeader"]!;
    var key = request.Headers[headerName].ToString();
    return key.Length > 0 && DevIdentityProvider.FixedTimeEquals(key, downstreams["vendor:ApiKey"]!)
        ? Results.Ok(new WhoAmI("vendor", $"API key in {headerName}", downstreams["vendor:KeyOwner"]!, null, null, null))
        : Results.Json(new { error = $"Missing or wrong {headerName}." }, statusCode: StatusCodes.Status401Unauthorized);
});

app.MapGet("/", () => Results.Text(
    "Downstream sample + dev identity provider. Get a user token at /dev/token?user=alice; " +
    "downstreams are /billing, /inventory, /reports, /legacy and /vendor (each /whoami)."));

app.Run();

// What a token-authenticated downstream saw: WHO the token is for (sub), who is ACTING on their behalf (the
// RFC 8693 'act' claim, present only on an exchanged token), and what the token was minted for.
static WhoAmI FromToken(string downstream, ClaimsPrincipal user)
{
    var actor = user.FindFirst("act")?.Value is { } act
        ? JsonDocument.Parse(act).RootElement.GetProperty("sub").GetString()
        : null;
    var subject = user.FindFirst("sub")!.Value;
    var credential = actor is not null ? "Bearer token (exchanged, acting for the user)"
        : subject == user.FindFirst("client_id")?.Value ? "Bearer token (client credentials)"
        : "Bearer token (the user's own)";
    return new WhoAmI(downstream, credential, subject, actor, user.FindFirst("aud")?.Value, user.FindFirst("scope")?.Value);
}

static IResult BasicChallenge() => new BasicChallengeResult();

namespace Downstream.Sample
{
    /// <summary>What a downstream authorized: the credential it accepted and the identity behind it.</summary>
    internal sealed record WhoAmI(
        string Downstream, string Credential, string Subject, string? ActingParty, string? Audience, string? Scope);

    /// <summary>A 401 carrying the WWW-Authenticate header a real Basic-protected service sends.</summary>
    internal sealed class BasicChallengeResult : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            httpContext.Response.Headers.WWWAuthenticate = "Basic realm=\"legacy\"";
            return Task.CompletedTask;
        }
    }
}
