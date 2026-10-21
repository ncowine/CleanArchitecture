# Calling downstream APIs: how each one authenticates

Every downstream gets **one** way to authenticate, chosen where its `HttpClient` is registered. Application
code asks `IHttpClientFactory` for the client by name and never sees which method is behind it.

```csharp
services.AddOnBehalfOf(configuration);   // once: this API's identity at the provider

services.AddHttpClient("billing",   c => c.BaseAddress = billingUri).AddOnBehalfOf();         // 1
services.AddHttpClient("reports",   c => c.BaseAddress = reportsUri).AddForwardedBearer();    // 2
services.AddHttpClient("inventory", c => c.BaseAddress = inventoryUri).AddClientCredentials(); // 3
services.AddHttpClient("legacy",    c => c.BaseAddress = legacyUri).AddBasicCredentials();    // 4
services.AddHttpClient("vendor",    c => c.BaseAddress = vendorUri).AddApiKeyCredentials();   // 5
```

## Choosing

Ask one question: **does the downstream need to know which user is calling?**

| The downstream needs…                                    | Use                       | It receives                                  |
|----------------------------------------------------------|---------------------------|----------------------------------------------|
| The user, and accepts **this API's** audience            | `AddForwardedBearer()`    | The caller's own token, unchanged            |
| The user, with its **own** audience                      | `AddOnBehalfOf()`         | A token for the user, minted for its audience |
| Only to know this API is calling                         | `AddClientCredentials()`  | This API's token, minted for its audience    |
| Legacy credentials only                                  | `AddBasicCredentials()`   | This API's service account, HTTP Basic       |
|                                                          | `AddApiKeyCredentials()`  | This API's key, in a header                  |

A downstream that accepts several methods still gets one. Pick from the table. **Do not chain methods**
(for example, "on a 401, retry with Basic"). That doubles the failure paths and quietly turns a call made
as the user into a call made as the service.

## Configuration

```jsonc
"OnBehalfOf": {
  "TokenEndpoint": "https://org.okta.com/oauth2/default/v1/token",
  "ClientId": "cleanarch-api",                       // ClientSecret / SigningKeyPem from a secret store
  "Downstreams": {
    "billing":   { "Audience": "api://billing",   "Scope": "invoices.read" },  // 1. On-Behalf-Of
    "inventory": { "Audience": "api://inventory", "Scope": "stock.read" }      // 3. client credentials
  }
},
"DownstreamCredentials": {
  "legacy": { "Username": "svc-cleanarch" },          // 4. Password from a secret store
  "vendor": { "ApiKeyHeader": "X-Vendor-Key" }        // 5. ApiKey from a secret store; header defaults to X-Api-Key
}
// 2. AddForwardedBearer() needs no configuration.
```

Secrets come from env (`DownstreamCredentials__legacy__Password`), Key Vault or Secrets Manager, or
user-secrets. Never put them in `appsettings.json`. The section name is the `HttpClient`'s name unless you
pass `downstreamName`. If a registered downstream is missing its values, **startup fails** with an error
that names the section. You don't find out later from a 401 on the far end.

## What fails, and how the API answers

| Situation                                                        | Exception                                          | Response |
|------------------------------------------------------------------|----------------------------------------------------|----------|
| Forwarded bearer, but the caller used an API key or Basic        | `CallerTokenRequiredException`                     | 401      |
| On-Behalf-Of, but the caller has no bearer token, or the IdP rejects it | `TokenExchangeException` (`SubjectRejected`)  | 401      |
| Client credentials rejected (wrong secret, scope not granted)    | `TokenExchangeException` (`ClientRejected`)        | 502      |
| Token endpoint down or returning 5xx                             | `TokenExchangeException` (`ProviderUnavailable`)   | 502      |

Each problem response carries a `downstream` field naming the downstream that failed.

## Caveats

- **Forwarding** sends a token whose `aud` is *this* API. A downstream that validates its audience properly
  will reject it unless it is deliberately set up to trust this API's audience. Forward only inside the
  same trust boundary; use On-Behalf-Of everywhere else. The token goes out with whatever lifetime it has
  left. Nothing refreshes it.
- **User vs service.** Forwarding and On-Behalf-Of need a request with a bearer caller. Client credentials,
  Basic and API key never read the request, so they also work from background work.
- **Never forward the caller's own Basic credentials** downstream, even though this API accepts Basic.
  The downstream gets this API's service account instead.
- **Tokens are cached in memory only.** On-Behalf-Of tokens are cached per user per downstream, and
  client-credentials tokens per downstream. They are re-fetched shortly before they expire and never
  written to a shared cache.

## Examples

- **Live.** [`samples/DownstreamAuth`](../samples/DownstreamAuth/README.md): run the sample and the API with
  the `downstream-demo` profile, then call `GET /demo/downstreams`. The API makes real calls with all five
  methods to services that validate each credential, plus one deliberately wrong call. The registrations
  and typed clients to copy are in `src/Api/CleanArch.Api/DownstreamDemo/`.
- **Tests.** `tests/CleanArch.Api.IntegrationTests/DownstreamAuthenticationTests.cs` covers the same five
  methods with the network faked, along with the failure cases and the startup checks.
