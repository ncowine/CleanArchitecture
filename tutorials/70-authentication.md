# Authentication and the Audit Actor

**Who this is for:** someone who needs to know who is calling their API — where several kinds
of caller exist and they don't all authenticate the same way.

**What you'll be able to do by the end:** support service callers, interactive users and
token-bearing clients side by side; store API keys so a database leak isn't a credential
leak; protect an endpoint; and understand why the audit trail is worthless until this guide
is done properly.

**What you need first:** a running application. Everything here is host-level — no module
changes.

---

## Table of contents

| # | Chapter | What you do there |
|---|---|---|
| 1 | [Two different questions](#1-two-different-questions) | Authentication vs authorization |
| 2 | [Three kinds of caller](#2-three-kinds-of-caller) | Why one scheme isn't enough |
| 3 | [Choosing a scheme per request](#3-choosing-a-scheme-per-request) | The policy scheme |
| 4 | [Step 1 — API keys](#4-step-1--api-keys) | Storage, hashing, minting, revocation |
| 5 | [Step 2 — Basic against a directory](#5-step-2--basic-against-a-directory) | Interactive callers |
| 6 | [Step 3 — OIDC bearer tokens](#6-step-3--oidc-bearer-tokens) | The modern option |
| 7 | [One principal, whatever the door](#7-one-principal-whatever-the-door) | Claims transformation |
| 8 | [Step 4 — Protect an endpoint](#8-step-4--protect-an-endpoint) | The easy part |
| 9 | [Why the audit trail depends on this](#9-why-the-audit-trail-depends-on-this) | The point of the whole guide |
| 10 | [Calling downstream as the user](#10-calling-downstream-as-the-user) | On-behalf-of |
| 11 | [Choosing a scheme for a new caller](#11-choosing-a-scheme-for-a-new-caller) | A decision table |
| 12 | [The checklist](#12-the-checklist) | Run this when doing it for real |
| 13 | [Troubleshooting](#13-troubleshooting) | Symptom, cause, fix |
| 14 | [Cheat sheet](#14-cheat-sheet) | Settings and commands |
| 15 | [Glossary](#15-glossary) | Every term used in this guide |

---

## 1. Two different questions

They get conflated constantly, and keeping them apart makes the rest of this straightforward.

| | Question | Produces |
|---|---|---|
| **Authentication** (authn) | *Who are you?* | An identity — a name |
| **Authorization** (authz) | *What are you allowed to do?* | A yes or no, per operation |

Authentication happens once per request, at the edge. Authorization happens wherever a
decision is needed.

In this codebase there is a third consumer of the answer, and it is the one people forget:
**the audit trail**. Every audited command records an `actor`, and that actor is whatever
authentication produced. Which means a weak authentication story doesn't just risk
unauthorised access — it silently makes your audit records unreliable. [Chapter 9](#9-why-the-audit-trail-depends-on-this)
returns to this.

---

## 2. Three kinds of caller

Real systems have callers with genuinely different constraints:

| Caller | Example | What it can hold | Natural credential |
|---|---|---|---|
| **A service** | A nightly report job, a scraper | A long-lived secret in config | **API key** |
| **A person** | Someone using an internal tool | Their existing corporate login | **Basic against a directory** |
| **A token client** | A SPA, a mobile app, another service via OAuth2 | A short-lived token | **OIDC bearer** |

Trying to force all three through one scheme is where authentication designs go wrong. A
background job cannot complete an interactive login. A person should not be pasting a
long-lived key. So this application supports all three, side by side.

```
   Incoming request
        │
        ├── has X-Api-Key header?          ──► API key scheme      (service)
        ├── has Authorization: Bearer ...? ──► OIDC bearer scheme  (token client)
        └── otherwise                      ──► Basic scheme        (person)
                                                     │
                                                     ▼
                                    one enriched principal, whatever the door
```

---

## 3. Choosing a scheme per request

ASP.NET Core normally has one default scheme. A **policy scheme** is a scheme whose only job
is to pick another one, per request:

```csharp
const string selectorScheme = "Smart";

services.AddAuthentication(selectorScheme)
    .AddPolicyScheme(selectorScheme, "ApiKey, Bearer, or Basic", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            if (context.Request.Headers.ContainsKey(ApiKeyAuthenticationHandler.HeaderName))
                return ApiKeyAuthenticationHandler.SchemeName;

            if (oktaConfigured && context.Request.Headers.Authorization.ToString()
                    .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return JwtBearerDefaults.AuthenticationScheme;

            return BasicAuthenticationHandler.SchemeName;
        };
    })
```

Read the order — it is a priority list, and the fall-through matters:

1. **`X-Api-Key` present** → the API-key scheme. Most specific, so it wins.
2. **`Authorization: Bearer …`** *and* the token scheme is configured → JWT.
3. **Everything else** → Basic, which is what a browser prompt and Swagger's Authorize
   dialog produce.

Note `oktaConfigured` in the second branch. The JWT scheme is only registered when
`Okta:Authority` is set, so on a machine with no identity provider a `Bearer` token falls
through to Basic and fails cleanly — rather than throwing "scheme not registered", which is a
much worse error to debug.

**Why a policy scheme rather than `[Authorize(AuthenticationSchemes = "A,B,C")]`:** the
multi-scheme attribute *tries* each scheme in turn, which means a failed API key produces a
challenge from a different scheme and a confusing `WWW-Authenticate` response. Selecting one
scheme up-front gives the caller the error that belongs to the credential they actually sent.

---

## 4. Step 1 — API keys

The right credential for a machine that must run unattended. Getting them right is mostly
about storage.

### Never store the key

```csharp
public static string Hash(string rawKey) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)));
```

Only the hash is persisted. A leaked database backup then contains no usable credentials.

> **Why SHA-256 and not bcrypt/Argon2?** This is the one place a fast hash is correct. The
> slow password hashes exist to throttle brute-forcing of **low-entropy human passwords**.
> An API key here is 256 bits from a CSPRNG — brute-forcing it is not on the table, and a
> slow hash would simply add latency to every single request. Use bcrypt for passwords, a
> fast hash for high-entropy secrets.

### Minting

```csharp
public static (string RawKey, string Prefix, string Hash) Generate()
{
    var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    var rawKey = KeyPrefix + secret;                    // "ca_live_…"
    return (rawKey, DisplayPrefix(rawKey), Hash(rawKey));
}
```

Three details worth copying:

- **`RandomNumberGenerator`**, not `Random`. A predictable key is not a key.
- **Base64url** (`-` and `_`, no padding) so the value is safe in a header or a URL.
- **A prefix** — `ca_live_` — which makes a leaked key greppable. Secret scanners in CI and
  on code hosts find keys by their prefix; a bare base64 blob is invisible to them.

The first twelve characters are stored in clear as `Prefix`, purely so a dashboard or a log
can say *which* key without revealing it.

Mint one with the host's operator command, which prints the raw key once and exits:

```bash
dotnet CleanArch.Api.dll --mint-api-key=reporting-service --mint-api-key-roles=service
```

**That output is the only time the key exists in readable form.** Lose it and you mint
another; there is no recovery path, by design.

### The record

```csharp
internal sealed class ApiKey
{
    public string Prefix { get; set; }          // visible, non-secret
    public string KeyHash { get; set; }         // the indexed lookup column
    public string Subject { get; set; }         // becomes the Name claim AND the audit actor
    public string Roles { get; set; }           // comma-separated
    public DateTime? ExpiresAtUtc { get; set; } // past this, rejected as if absent
    public DateTime? RevokedAtUtc { get; set; } // kill switch
    public DateTime? LastUsedAtUtc { get; set; }// best-effort "last seen"
}
```

`ExpiresAtUtc` and `RevokedAtUtc` are what separate a key store from a hard-coded constant.
A key you cannot revoke is a key you can never rotate.

`LastUsedAtUtc` answers "is anything still using this?" — the question that otherwise stops
every cleanup effort dead.

### Where they live

```csharp
services.AddDbContext<ApiKeyDbContext>(options =>
    options.UseSqlite(apiKeyConnectionString,
        sqlite => sqlite.MigrationsHistoryTable(ApiKeyDbContext.MigrationsHistoryTable)));
```

The keys share the Students database file but sit behind a **dedicated `DbContext` with its
own migrations-history table**. So an authentication concern never entangles a business
module's schema, and the two can be migrated independently despite sharing a file.

### Validation, and why it's cached

Validation runs on **every request**, so a database round trip per call is a real cost:

```csharp
services.AddScoped<DbApiKeyValidator>();
services.AddScoped<IApiKeyValidator>(provider => new CachingApiKeyValidator(
    provider.GetRequiredService<DbApiKeyValidator>(),
    provider.GetRequiredService<HybridCache>()));
```

A concrete validator wrapped in a caching decorator — the same shape used elsewhere in this
codebase for the directory lookup. The decorator knows nothing about databases; the validator
knows nothing about caching.

> **The trade-off, stated plainly:** the cache has a short TTL, so a revoked key keeps working
> until it expires. That window is the price of not hitting the database on every request. It
> should be seconds, not minutes, and you should know what it is before you need to revoke
> something in a hurry.

### The development keys

```csharp
private static readonly (string RawKey, string Subject, string Roles)[] DevelopmentKeys =
{
    ("dev-api-key-reporting",   "reporting-service",   "service"),
    ("dev-api-key-integration", "integration-service", "service"),
};
```

Seeded **only in Development**, idempotently. They are real rows going through the real
validation path — so local development exercises the same code as production, rather than a
bypass that hides bugs.

They are also published in this repository's documentation, which is exactly why they must
never be seeded outside Development. An application accidentally running with
`ASPNETCORE_ENVIRONMENT=Development` in production is wide open to anyone who has read the
README — and that is the single highest-value thing to verify after a deploy.

---

## 5. Step 2 — Basic against a directory

For people. The caller sends `Authorization: Basic base64(user:password)`; the handler binds
those credentials against Active Directory.

```csharp
services.AddSingleton<ICredentialValidator, ActiveDirectoryCredentialValidator>();
```

**Basic is only acceptable over TLS.** The credentials are base64, which is encoding, not
encryption — anyone on the path reads them. Over HTTPS on an internal network it is a
reasonable, low-ceremony choice for an internal tool; over plain HTTP it is a password
disclosure with extra steps.

The directory does double duty — it authenticates *and* it supplies group memberships for
authorization:

```csharp
services.AddScoped<ActiveDirectoryUserDirectory>();
services.AddScoped<IUserDirectory>(provider => new CachingUserDirectory(
    provider.GetRequiredService<ActiveDirectoryUserDirectory>(),
    provider.GetRequiredService<HybridCache>()));
```

Cached, for the same reason as API keys: a directory lookup per request is expensive.

Both are Windows-only APIs, so they sit behind a platform guard with an in-memory fallback:

```csharp
if (OperatingSystem.IsWindows())
    AddActiveDirectory(services);
else
    services.AddSingleton<IUserDirectory, FakeUserDirectory>();
```

That is what lets the application boot and its tests run on a Linux build agent.

---

## 6. Step 3 — OIDC bearer tokens

The modern option, and the right default for new work. The caller obtains a token from an
identity provider and sends it; your application only *verifies* it.

```csharp
authBuilder.AddJwtBearer(options =>
{
    options.Authority = oktaAuthority;
    options.Audience = oktaAudience;
    options.SaveToken = true;
    options.MapInboundClaims = false;
    options.TokenValidationParameters.NameClaimType = configuration["Okta:NameClaim"] ?? "sub";
});
```

Each line is load-bearing:

**`Authority`** is the issuer URL. Setting it makes the framework fetch the provider's OIDC
metadata and signing keys, then validate the **signature, issuer, audience and lifetime** on
every request. You never handle a key yourself, and keys rotate without a deployment.

**`Audience`** is who the token was minted *for*. Without it, a valid token issued for a
different application in the same tenant would be accepted here — a real and frequently
missed hole.

**`SaveToken = true`** keeps the validated token on the request so it can be exchanged later
([chapter 10](#10-calling-downstream-as-the-user)).

**`MapInboundClaims = false`** stops the legacy remapping of JWT claim names into long
WS-Federation URIs, so `sub` stays `sub`.

**`NameClaimType`** is the one that will bite you. Okta access tokens carry the user in `sub`
and have **no `name` claim** — so without this line, `User.Identity.Name` is `null`. Nothing
throws. Authentication succeeds. And your audit trail records an empty actor for every
token-authenticated request.

The whole scheme is enabled only when configured:

```csharp
var oktaConfigured = !string.IsNullOrWhiteSpace(oktaAuthority);
```

Set `Okta__Authority` and `Okta__Audience` to turn it on.

> **For service-to-service auth, prefer this over API keys.** The OAuth2
> **client-credentials** grant gets a machine a short-lived token from the same identity
> provider people use — so machines and humans share one revocation story, one audit story
> and one rotation story. Long-lived API keys are a reasonable fallback when the caller
> cannot do OAuth2, not a first choice.

---

## 7. One principal, whatever the door

Three schemes would normally mean three differently-shaped principals, and every downstream
consumer having to care which. A **claims transformation** normalises them:

```csharp
services.AddScoped<IClaimsTransformation, ActiveDirectoryClaimsTransformation>();
```

It runs *after* whichever scheme succeeded, looks the identity up in the directory, and adds
the display name and the roles derived from group membership.

So by the time your endpoint runs, the principal is fully populated whether the caller sent
an API key, a password, or a token. Authorization policies and the audit actor read one
shape and never branch on how the caller got in.

**Why this matters:** without it, `[Authorize(Roles = "…")]` would work for directory users
and silently fail for token callers, and you would end up writing per-scheme special cases
into business code — which is exactly where authentication logic must never end up.

---

## 8. Step 4 — Protect an endpoint

```csharp
group.MapPost("/students", async (CreateStudent.Command command, ISender sender, CancellationToken ct) =>
{
    var id = await sender.Send(command, ct);
    return Results.Created($"/students/{id}", new { id });
})
.RequireAuthorization();
```

That's it: authenticated callers only, by any scheme. For a role:

```csharp
.RequireAuthorization(policy => policy.RequireRole("service"));
```

The convention in this codebase: **every write requires authorization; reads are open.** That
is a deliberate POC choice, not a recommendation — a real deployment usually authorizes reads
too, because a read endpoint that returns personal data is not less sensitive than the write
that created it.

Applying it to a group rather than each endpoint is safer, because the failure mode of
forgetting one endpoint is silent:

```csharp
var group = app.MapGroup("/students").RequireAuthorization();
```

---

## 9. Why the audit trail depends on this

Here is the chain that makes this guide matter more than it looks:

```
   Authentication produces a principal
        │
        ▼
   ICurrentActor reads it            ──► "who" on every audit record
        │
        ▼
   HttpContextActor:
        1. authenticated user's name   ← trustworthy
        2. else the X-Actor header     ← ANYONE CAN SET THIS
        3. else "system"
```

Step 2 is a development convenience and it is spoofable by definition. An unauthenticated
caller can claim to be anyone by adding a header.

The consequence: **an endpoint that is audited but not authorized produces audit records that
cannot be trusted.** They look identical to real ones. There is no flag on the record saying
"this actor was self-declared".

The fix is not in the auditing code. It is `.RequireAuthorization()` on every auditable
command, so step 1 always wins and step 2 is never reached.

For token callers there is a second failure with the same shape: get `NameClaimType` wrong
([chapter 6](#6-step-3--oidc-bearer-tokens)) and `Identity.Name` is null, so the actor falls
through to the header or to `"system"` — for every request, silently.

Two things to verify after wiring authentication, and neither is optional:

1. Call an audited endpoint authenticated, and confirm the audit record's `actor` is the
   real identity.
2. Call it *without* credentials and confirm you get a `401` rather than a record attributed
   to `system`.

---

## 10. Calling downstream as the user

When your API must call another API **as the user who called you** — not as itself — the
pattern is OAuth2 **On-Behalf-Of**: exchange the caller's token for a new one scoped to the
downstream service.

```csharp
builder.Services.AddHttpClient("Downstream", client =>
        client.BaseAddress = new Uri(builder.Configuration["DownstreamApi:BaseUrl"]!))
    .AddHttpMessageHandler<OnBehalfOfHandler>();
```

The handler lifts the validated token off the request (which is what `SaveToken = true` was
for), exchanges it at the identity provider, caches the result, and attaches it to the
outgoing call.

**Why not just forward the original token?** Because it was issued for *your* audience. The
downstream service should reject it — and if it doesn't, any service holding a token for you
can impersonate your users everywhere. The exchange produces a token scoped to exactly one
downstream audience, so a compromised service can only reach what it was allowed to reach.

Configuration lives under `OnBehalfOf` — token endpoint, client id, and either a client
secret or a signed assertion (`PrivateKeyJwt`). Both are secrets; both come from the
environment or a secret store, never from `appsettings.json`.

---

## 11. Choosing a scheme for a new caller

| The caller is… | Use | Because |
|---|---|---|
| A background job you own | **OAuth2 client-credentials**, else an API key | Shares the identity provider's revocation and rotation |
| A third party integrating with you | **API key** | No federation to set up; you can revoke it unilaterally |
| A person in an internal tool | **Basic against the directory**, over TLS | Their existing login; no new password to manage |
| A SPA or mobile app | **OIDC bearer** (Authorization Code + PKCE) | Never holds a long-lived secret |
| Another service in an OAuth2 estate | **OIDC bearer** | One identity story for everything |
| Your monitoring scraper | **API key**, plus a network restriction | Simple, and network-scoped anyway |

And a rule that survives all of them: **the credential's lifetime should match the caller's
supervision.** A token for something a person is watching can be short. A key for an
unattended job must be revocable, because it will outlive whoever created it.

---

## 12. The checklist

Configuration:

- [ ] `ASPNETCORE_ENVIRONMENT=Production` outside development — **verify it**, don't assume
- [ ] Confirm the dev API keys are *not* accepted in production
- [ ] `Okta__Authority` and `Okta__Audience` set if token callers exist
- [ ] `NameClaimType` matches what your provider actually issues
- [ ] All secrets from environment or a secret store, never `appsettings.json`

API keys:

- [ ] Only hashes stored; raw keys shown once at minting
- [ ] Keys minted with the CSPRNG path, never hand-invented
- [ ] Every key has a `Subject` that reads well as an audit actor
- [ ] Expiry set where the caller is time-bounded
- [ ] You know the validation-cache TTL — it is your revocation delay

Endpoints:

- [ ] Every write requires authorization
- [ ] Every **audited** command requires authorization — see [chapter 9](#9-why-the-audit-trail-depends-on-this)
- [ ] Applied at the group level so a new endpoint inherits it
- [ ] Basic only ever over TLS

Verify:

- [ ] An audited call records the real identity as `actor`
- [ ] An unauthenticated call gets `401`, not a record attributed to `system`
- [ ] A revoked key stops working within the cache TTL
- [ ] A token for the wrong audience is rejected

---

## 13. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `401` with a key you just minted | Cache still holds the previous negative result, or the key was mistyped | Wait out the TTL; keys are case-sensitive |
| Key works then stops | Expired or revoked | Check `ExpiresAtUtc` / `RevokedAtUtc` |
| Bearer token gets `401`, no detail | The JWT scheme isn't registered | Set `Okta:Authority` |
| Token valid but `actor` is empty | `NameClaimType` doesn't match the provider's claims | Set `Okta__NameClaim` (usually `sub`) |
| Token from another app is accepted | `Audience` not set | Set it — this is a real hole |
| Basic prompts a browser dialog you didn't expect | Fall-through: no `X-Api-Key`, no `Bearer` | Expected; send a credential |
| Roles empty for token callers | Claims transformation couldn't resolve the identity | Check the name claim resolves to a directory user |
| Everything is `system` in the audit trail | The endpoints aren't authorized | `.RequireAuthorization()` |
| Works on Windows, not on the build agent | AD APIs are Windows-only | The fake directory is the fallback — expected |
| Dev keys work in production | The app is running in Development | Fix `ASPNETCORE_ENVIRONMENT` immediately |

---

## 14. Cheat sheet

### Settings

| Key | Purpose |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | **`Production`** outside dev. Gates the seeded dev keys |
| `Okta__Authority` | OIDC issuer URL. Presence enables the JWT scheme |
| `Okta__Audience` | Who tokens must be issued for |
| `Okta__NameClaim` | Which claim becomes `Identity.Name`. Default `sub` |
| `ActiveDirectory__Server` / `__Container` | Directory to bind and search against |
| `OnBehalfOf__*` | Token exchange for downstream calls. **Secret** |

### Schemes

| Header sent | Scheme selected | Caller |
|---|---|---|
| `X-Api-Key: …` | ApiKey | Service |
| `Authorization: Bearer …` | JWT (when configured) | Token client |
| Anything else | Basic | Person |

### Commands

```bash
# Mint a production key — prints the raw key ONCE, then exits
dotnet CleanArch.Api.dll --mint-api-key=reporting-service --mint-api-key-roles=service

# Call with a key
curl -H "X-Api-Key: ca_live_..." http://localhost:5235/students

# Verify you are NOT in Development (expect a non-200)
curl -o /dev/null -w '%{http_code}\n' http://localhost:5235/swagger
```

### Protecting endpoints

```csharp
var group = app.MapGroup("/things").RequireAuthorization();   // prefer group level
endpoint.RequireAuthorization(policy => policy.RequireRole("service"));
```

---

## 15. Glossary

| Term | Meaning |
|---|---|
| **Actor** | Who performed an action, as recorded in the audit trail |
| **Audience** | Who a token was issued for. Must be validated |
| **Authentication (authn)** | Establishing who the caller is |
| **Authorization (authz)** | Deciding what the caller may do |
| **Authority** | The OIDC issuer URL; supplies signing keys via discovery |
| **Basic authentication** | Base64 `user:password` in a header. TLS only |
| **Bearer token** | A token that grants access to whoever holds it |
| **Claim** | One statement about an identity — name, role, subject |
| **Claims transformation** | Code that enriches a principal after authentication |
| **Client credentials** | OAuth2 grant for machine callers |
| **CSPRNG** | Cryptographically secure random generator. What key material must come from |
| **JWT** | JSON Web Token — the usual bearer token format |
| **On-Behalf-Of** | Exchanging a caller's token for one scoped to a downstream service |
| **OIDC** | OpenID Connect — the identity layer over OAuth2 |
| **PKCE** | The proof key that lets public clients use Authorization Code safely |
| **Policy scheme** | A scheme that selects another scheme per request |
| **Principal** | The authenticated identity plus its claims |
| **Scheme** | One way of authenticating — ApiKey, Basic, Bearer |
| **Subject (`sub`)** | The identity a token represents |

---

## Where to go next

- **[Auditing](40-auditing.md)** — what the actor this guide establishes is used for, and why
  it is the weakest link.
- **[Adding a feature](20-add-a-feature.md)** — where `.RequireAuthorization()` goes.
