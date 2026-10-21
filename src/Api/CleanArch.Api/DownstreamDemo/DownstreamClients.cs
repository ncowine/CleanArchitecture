namespace CleanArch.Api.DownstreamDemo;

// Typed clients for the downstream APIs in the live demo — the shape to copy for a real downstream. Callers
// depend on the interface; the implementation is handed an HttpClient that already has its base address and
// its authentication handler (see DownstreamDemoEndpoints.AddDownstreamDemo), so none of these classes knows
// or cares HOW the call is authenticated. In a module, the interface belongs in Application and the
// implementation in Infrastructure.

/// <summary>What a downstream reported it authorized — returned by each sample service's <c>/whoami</c>.</summary>
public sealed record DownstreamIdentity(
    string Downstream, string Credential, string Subject, string? ActingParty, string? Audience, string? Scope);

/// <summary>Billing: authorizes per user, with its own audience → called On-Behalf-Of the user.</summary>
public interface IBillingApi
{
    Task<DownstreamIdentity> WhoAmIAsync(CancellationToken cancellationToken);
}

/// <summary>Reports: authorizes per user and accepts this API's own audience → the caller's token is forwarded.</summary>
public interface IReportsApi
{
    Task<DownstreamIdentity> WhoAmIAsync(CancellationToken cancellationToken);
}

/// <summary>Inventory: only needs to know this API is calling → client credentials.</summary>
public interface IInventoryApi
{
    Task<DownstreamIdentity> WhoAmIAsync(CancellationToken cancellationToken);
}

/// <summary>Legacy: only takes a service account over HTTP Basic.</summary>
public interface ILegacyApi
{
    Task<DownstreamIdentity> WhoAmIAsync(CancellationToken cancellationToken);
}

/// <summary>Vendor: only takes an API key, in a header of its own choosing.</summary>
public interface IVendorApi
{
    Task<DownstreamIdentity> WhoAmIAsync(CancellationToken cancellationToken);
}

internal sealed class BillingApi(HttpClient http) : IBillingApi
{
    public async Task<DownstreamIdentity> WhoAmIAsync(CancellationToken cancellationToken) =>
        (await http.GetFromJsonAsync<DownstreamIdentity>("whoami", cancellationToken))!;
}

internal sealed class ReportsApi(HttpClient http) : IReportsApi
{
    public async Task<DownstreamIdentity> WhoAmIAsync(CancellationToken cancellationToken) =>
        (await http.GetFromJsonAsync<DownstreamIdentity>("whoami", cancellationToken))!;
}

internal sealed class InventoryApi(HttpClient http) : IInventoryApi
{
    public async Task<DownstreamIdentity> WhoAmIAsync(CancellationToken cancellationToken) =>
        (await http.GetFromJsonAsync<DownstreamIdentity>("whoami", cancellationToken))!;
}

internal sealed class LegacyApi(HttpClient http) : ILegacyApi
{
    public async Task<DownstreamIdentity> WhoAmIAsync(CancellationToken cancellationToken) =>
        (await http.GetFromJsonAsync<DownstreamIdentity>("whoami", cancellationToken))!;
}

internal sealed class VendorApi(HttpClient http) : IVendorApi
{
    public async Task<DownstreamIdentity> WhoAmIAsync(CancellationToken cancellationToken) =>
        (await http.GetFromJsonAsync<DownstreamIdentity>("whoami", cancellationToken))!;
}
