# Live demo: calling downstream APIs with each authentication method

The CleanArch API makes **real HTTP calls** to five downstream services. Each one accepts a different kind
of credential, and each reports back who it authorized. Everything runs on this machine. You don't need an
Okta tenant or Docker, because `Downstream.Sample` also plays the identity provider.

| Downstream  | Accepts                              | API registration (`DownstreamDemo/DownstreamDemoEndpoints.cs`) |
|-------------|--------------------------------------|-----------------------------------------------------------------|
| `billing`   | Bearer token for `api://billing`     | `AddHttpClient<IBillingApi, BillingApi>("billing", …).AddOnBehalfOf()` |
| `reports`   | Bearer token for `cleanarch-api` (the API's own audience) | `….AddForwardedBearer()`                   |
| `inventory` | Bearer token for `api://inventory`   | `….AddClientCredentials()`                                     |
| `legacy`    | HTTP Basic, `svc-cleanarch`          | `….AddBasicCredentials()`                                      |
| `vendor`    | API key in `X-Vendor-Key`            | `….AddApiKeyCredentials()`                                     |

The decision table and caveats are in [docs/downstream-authentication.md](../../docs/downstream-authentication.md).

## Run it

```powershell
# 1. Start the downstreams and the dev identity provider (https://localhost:7300)
dotnet run --project samples/DownstreamAuth/Downstream.Sample

# 2. In a second terminal, start the API pointed at them (https://localhost:7214/swagger)
dotnet run --project src/Api/CleanArch.Api --launch-profile downstream-demo
```

The `downstream-demo` profile (`src/Api/CleanArch.Api/Properties/launchSettings.json`) sets every value the
API needs. It uses the same keys as a real deployment: `Okta:Authority` points at the sample instead of
Okta, and `OnBehalfOf:*` and `DownstreamCredentials:*` hold the sample's dev-only secrets. Without that
profile, the demo endpoint and clients are not registered at all.

## Try it

**As a user.** Get a token for alice (this stands in for an interactive login):

```powershell
curl.exe -k "https://localhost:7300/dev/token?user=alice"
```

In Swagger, open **Authorize → Bearer**, paste `access_token`, and run **Demo → GET /demo/downstreams**.
With curl instead:

```powershell
$token = (curl.exe -sk "https://localhost:7300/dev/token?user=alice" | ConvertFrom-Json).access_token
curl.exe -sk -H "Authorization: Bearer $token" https://localhost:7214/demo/downstreams
```

What you'll see:

| Call | Downstream reports |
|---|---|
| billing (On-Behalf-Of) | subject `alice`, acting party `cleanarch-api`, audience `api://billing`. The user, re-scoped to billing |
| reports (forwarded) | subject `alice`, audience `cleanarch-api`. The user's own token, unchanged |
| inventory (client credentials) | subject `cleanarch-api`, audience `api://inventory`. The service; no user involved |
| legacy (Basic) | subject `svc-cleanarch`. The service account |
| vendor (API key) | subject `cleanarch-api`, via `X-Vendor-Key` |
| billing via forwarded bearer (deliberately wrong) | **401**, `The audience 'cleanarch-api' is invalid`. This is why On-Behalf-Of exists |

**As a service.** Call the same endpoint with `X-Api-Key: dev-api-key-reporting`. The three service-identity
calls still succeed. Billing and reports are refused *before anything is sent*, because an API-key caller
has no user token to exchange or forward. The API never falls back to its own identity.

## What the sample is (and isn't)

- **Dev identity provider** (`DevIdentityProvider.cs`). It provides OIDC discovery, a JWKS endpoint,
  `POST /connect/token` (RFC 8693 token exchange and `client_credentials`, with the client authenticated by
  HTTP Basic) and `GET /dev/token`. The signing key is generated at startup. If you restart the sample, get
  a new user token. The API picks up the new key by itself.
- **Downstreams** (`Program.cs`). Each one validates its credential for real (signature, issuer and
  audience for tokens; constant-time comparison for Basic and the API key) and returns what it authorized.
- **Not** a real identity provider. It has no users, consent, refresh tokens or key persistence. Every
  secret in `appsettings.json` is published on purpose. Use it for local development only.
