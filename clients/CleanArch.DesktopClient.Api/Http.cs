using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CleanArch.DesktopClient.Api;

/// <summary>
/// A failure talking to the API, already translated into a message that's safe to show a user.
/// Covers both transport failures (the API is down/unreachable) and error responses (4xx/5xx),
/// with the server's RFC 7807 problem detail surfaced where one is available.
/// </summary>
public sealed class ApiException : Exception
{
    public ApiException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException) => StatusCode = statusCode;

    /// <summary>The HTTP status code, when the call reached the server; null for transport failures.</summary>
    public int? StatusCode { get; }

    internal static ApiException Unreachable(Exception inner) => new(
        "Can't reach the server. Make sure the API is running and try again.", statusCode: null, inner);

    internal static ApiException Timeout(Exception inner) => new(
        "The request timed out — the server may be unavailable.", statusCode: null, inner);

    /// <summary>Build an exception from a non-success response, preferring the server's problem detail.</summary>
    internal static async Task<ApiException> FromResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        var detail = await TryReadProblemDetailAsync(response, ct);
        return new ApiException(detail ?? DefaultMessage(response.StatusCode), status);
    }

    private static async Task<string?> TryReadProblemDetailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentType?.MediaType is not ("application/problem+json" or "application/json"))
        {
            return null;
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = document.RootElement;

            // Validation problems carry per-field messages under "errors"; flatten them.
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                var messages = errors.EnumerateObject()
                    .SelectMany(field => field.Value.EnumerateArray().Select(m => m.GetString()))
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .ToArray();
                if (messages.Length > 0)
                {
                    return string.Join(" ", messages);
                }
            }

            // Domain rule violations carry the message under "detail"; titles are the last resort.
            if (root.TryGetProperty("detail", out var detailProperty) && detailProperty.ValueKind == JsonValueKind.String)
            {
                return detailProperty.GetString();
            }

            if (root.TryGetProperty("title", out var titleProperty) && titleProperty.ValueKind == JsonValueKind.String)
            {
                return titleProperty.GetString();
            }
        }
        catch (JsonException)
        {
            // Body wasn't the problem-detail shape we expected; fall back to a status-based message.
        }

        return null;
    }

    private static string DefaultMessage(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "You're not authorized. Please sign in again.",
        HttpStatusCode.NotFound => "The requested item was not found.",
        >= HttpStatusCode.InternalServerError => "The server had a problem. Please try again.",
        _ => $"The request failed ({(int)status} {status}).",
    };
}

/// <summary>
/// Sends requests and turns every failure mode into an <see cref="ApiException"/> with a user-safe
/// message, so the typed clients (and the ViewModels above them) never have to interpret raw
/// <see cref="HttpRequestException"/>s or discarded error bodies.
/// </summary>
internal static class ApiHttp
{
    public static async Task<TResult> GetJsonAsync<TResult>(this HttpClient http, string uri, CancellationToken ct)
    {
        using var response = await SendAsync(() => http.GetAsync(uri, ct), ct);
        return (await response.Content.ReadFromJsonAsync<TResult>(ct))!;
    }

    /// <summary>
    /// A GET for a single item where "not found" is a normal, non-error outcome: a 404 returns the
    /// default (null) so callers can render an empty view rather than surface an error.
    /// </summary>
    public static async Task<TResult?> GetJsonOrNotFoundAsync<TResult>(this HttpClient http, string uri, CancellationToken ct)
    {
        using var response = await SendRawAsync(() => http.GetAsync(uri, ct), ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await ApiException.FromResponseAsync(response, ct);
        }

        return await response.Content.ReadFromJsonAsync<TResult>(ct);
    }

    public static async Task PostAsync(this HttpClient http, string uri, object? body, CancellationToken ct)
    {
        using var _ = await SendJsonAsync(http, uri, body, ct);
    }

    public static async Task<TResult> PostJsonAsync<TResult>(this HttpClient http, string uri, object? body, CancellationToken ct)
    {
        using var response = await SendJsonAsync(http, uri, body, ct);
        return (await response.Content.ReadFromJsonAsync<TResult>(ct))!;
    }

    private static Task<HttpResponseMessage> SendJsonAsync(HttpClient http, string uri, object? body, CancellationToken ct) =>
        SendAsync(() => body is null ? http.PostAsync(uri, content: null, ct) : http.PostAsJsonAsync(uri, body, ct), ct);

    private static async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        var response = await SendRawAsync(send, ct);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var failure = await ApiException.FromResponseAsync(response, ct);
        response.Dispose();
        throw failure;
    }

    /// <summary>Send, translating only transport failures; the caller decides what counts as an error status.</summary>
    private static async Task<HttpResponseMessage> SendRawAsync(Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        try
        {
            return await send();
        }
        catch (HttpRequestException exception)
        {
            // Connection refused, DNS failure, TLS failure — the API is down or unreachable.
            throw ApiException.Unreachable(exception);
        }
        catch (TaskCanceledException exception) when (!ct.IsCancellationRequested)
        {
            // The cancellation came from the HttpClient timeout, not the caller.
            throw ApiException.Timeout(exception);
        }
    }
}
