using System.Net.Http;
using System.Net.Http.Json;

namespace Poc3.SignalR;

/// <summary>
/// Reads and changes go through the API over plain HTTP.
/// </summary>
/// <remarks>
/// ✅ DO send every change as an HTTP request. The API validates it, saves it, audits it, and THEN tells every connected
///    client — including this one — over SignalR.
/// ❌ DON'T send changes over the hub connection ("hub methods that write"). You'd bypass the API's validation,
///    authorization, auditing and transactions, and you'd have two ways to change data to keep in step.
/// </remarks>
public sealed class EquipmentApi
{
    private readonly HttpClient _http;

    /// <param name="http">One client for the app's lifetime, configured in App.xaml.cs with the base address and the
    /// X-Api-Key / X-Actor headers.</param>
    public EquipmentApi(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<EquipmentDto>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        // The list endpoint is POST with the paging in the body (the house style for list endpoints).
        using HttpResponseMessage response = await _http.PostAsJsonAsync(
            "equipment/search", new { page = 1, pageSize = 100 }, cancellationToken);
        response.EnsureSuccessStatusCode();

        PagedResult<EquipmentDto>? page = await response.Content.ReadFromJsonAsync<PagedResult<EquipmentDto>>(cancellationToken);
        return page?.Items ?? [];
    }

    public async Task CreateLaptopAsync(CancellationToken cancellationToken = default)
    {
        string tag = $"POC-{Random.Shared.Next(100000, 999999)}";

        // category 0 = Laptop (the API takes the enum as a number).
        using HttpResponseMessage response = await _http.PostAsJsonAsync(
            "equipment", new { name = $"POC laptop {tag}", category = 0, assetTag = tag }, cancellationToken);
        await EnsureSuccess(response);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _http.DeleteAsync($"equipment/{id}", cancellationToken);
        await EnsureSuccess(response);
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            // The API returns problem details; show them rather than a bare status code.
            string body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}: {body}", null, response.StatusCode);
        }
    }
}
