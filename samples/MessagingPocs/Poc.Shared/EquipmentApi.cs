using System.Net;
using System.Net.Http;
using System.Net.Http.Json;

namespace Poc.Shared;

/// <summary>
/// Reads and changes equipment through the API over plain HTTP. Every POC uses it the same way.
/// </summary>
/// <remarks>
/// ✅ DO send every change to the API's data as an HTTP request. The API validates it, saves it, audits it, and THEN
///    tells everyone — over RabbitMQ and SignalR, this app included.
/// ❌ DON'T send a change to the API's data over the SignalR hub, or invent broker messages for it. You'd bypass the
///    API's validation, authorization, auditing and transactions. (The net472 app's EquipmentDisposed is the exception
///    that proves the rule: an EXISTING legacy event the API was taught to handle, through the same command.)
/// </remarks>
public sealed class EquipmentApi
{
    private readonly HttpClient http;

    public EquipmentApi(HttpClient http)
    {
        this.http = http;
    }

    /// <summary>
    /// One HttpClient for the life of the app.
    /// ✅ DO keep one, with a pooled-connection lifetime so a changed DNS entry (a server move, a failover) is picked up
    ///    within minutes.
    /// ❌ DON'T create a new HttpClient per call: each opens its own connections, and under load the machine runs out
    ///    of sockets.
    /// ❌ DON'T expect AddHttpClient&lt;T&gt;() to refresh DNS for a client held by a singleton (like a view model): the
    ///    factory's handler rotation never happens for it.
    /// </summary>
    public static EquipmentApi Create(ApiOptions options)
    {
#if NETFRAMEWORK
        // .NET Framework has no SocketsHttpHandler: the connection lease on the ServicePoint does the same job.
        Uri baseUrl = new(options.BaseUrl);
        System.Net.ServicePointManager.FindServicePoint(baseUrl).ConnectionLeaseTimeout = (int)TimeSpan.FromMinutes(5).TotalMilliseconds;
        HttpClient http = new()
#else
        HttpClient http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
#endif
        {
            BaseAddress = new Uri(options.BaseUrl),
        };
        http.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);   // write endpoints require it
        http.DefaultRequestHeaders.Add("X-Actor", options.Actor);      // who did it, in the API's audit trail
        return new EquipmentApi(http);
    }

    public async Task<List<EquipmentRow>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        // The list endpoint is POST with the paging in the body (the house style for list endpoints).
        using HttpResponseMessage response = await http.PostAsJsonAsync(
            "equipment/search", new { page = 1, pageSize = 100 }, cancellationToken);
        response.EnsureSuccessStatusCode();

        PagedResult? page = await response.Content.ReadFromJsonAsync<PagedResult>(cancellationToken);
        return page?.Items ?? [];
    }

    public async Task CreateLaptopAsync(string createdBy, CancellationToken cancellationToken = default)
    {
        string tag = $"POC-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant()}";

        // category 0 = Laptop (the API takes the enum as a number).
        using HttpResponseMessage response = await http.PostAsJsonAsync(
            "equipment", new { name = $"Laptop from {createdBy}", category = 0, assetTag = tag }, cancellationToken);
        await EnsureSuccess(response);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await http.DeleteAsync($"equipment/{id}", cancellationToken);

        // Already gone (someone else deleted it first) is fine: the outcome the user wanted.
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            await EnsureSuccess(response);
        }
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            // The API returns problem details; show them rather than a bare status code.
            string body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }
    }

    private sealed record PagedResult(List<EquipmentRow> Items, int Page, int PageSize, int TotalCount);
}

/// <summary>One row of the list, as the API's search returns it.</summary>
public sealed record EquipmentRow(Guid Id, string Name, string Category, string AssetTag, string Status);
