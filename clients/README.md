# CleanArch Desktop Client (WPF + Prism 8)

A WPF desktop client that consumes the CleanArchitecture API, built with **Prism 8.1.97** (the last
MIT-licensed Prism) using MVVM, with unit-tested ViewModels. It has its own solution and its own
`Directory.Build.props`/`Directory.Packages.props`, so it's isolated from the API's strict build settings.

## Projects

| Project | TFM | Role |
|---|---|---|
| `CleanArch.DesktopClient.Api` | net10.0 | Typed API clients (`IStudentsApiClient`, `ILibraryApiClient`), DTOs, JWT bearer handler, dev token store. No WPF dependency. |
| `CleanArch.DesktopClient` | net10.0-windows | WPF + Prism (DryIoc). Shell + region navigation, Views, ViewModels, DI bootstrap. |
| `CleanArch.DesktopClient.Tests` | net10.0-windows | xUnit tests for the ViewModels (fakes for the API clients + navigation). |

## Architecture

- **MVVM**: ViewModels derive from `BindableBase`, use `DelegateCommand`, and depend only on abstractions
  (`IStudentsApiClient`, `ILibraryApiClient`, `INavigationService`) — never on WPF — so they're unit-testable
  with no UI thread. A `ViewModelBase` provides shared `IsBusy`/`Error` and a guarded async runner.
- **Navigation**: a thin `INavigationService` over Prism's region manager keeps ViewModels off the large
  `IRegionManager` surface (and trivially fakeable in tests). Views auto-register via `RegisterForNavigation`.
- **Auth**: `DevTokenStore` calls the API's `/dev/token` to get a JWT; `AuthHeaderHandler` attaches it (and a
  correlation id) to every request. Swap `DevTokenStore` for an OIDC flow (e.g. IdentityModel.OidcClient,
  Authorization Code + PKCE) for production — nothing else changes.
- **HTTP**: a single long-lived authenticated `HttpClient` (constructed in `ApiClientFactory`). Desktop apps
  don't need `IHttpClientFactory` pooling the way servers do, and Prism's container isn't `IServiceCollection`.

## Screens

Login (dev token) → Students (paged search, withdraw, navigate) → Student detail / Loans (borrow, assess fine).

## Running

The client points at `http://localhost:5080/` (see `App.ApiBaseUrl`). Start the API first, then run the
client from Visual Studio / `dotnet run --project CleanArch.DesktopClient`. (A display is required to show
the window; the build and tests run headless.)

```bash
dotnet test    # runs the ViewModel unit tests
```
