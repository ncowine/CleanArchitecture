using System.Security.Claims;
using Asp.Versioning;
using Asp.Versioning.Builder;
using CleanArch.Api.Authentication;

namespace CleanArch.Api.DownstreamDemo;

/// <summary>
/// A LIVE example of every downstream authentication method: real HTTP calls from this API to the services in
/// <c>samples/DownstreamAuth/Downstream.Sample</c>, which also plays the identity provider. Development only,
/// and off unless <c>DownstreamDemo:BaseUrl</c> is set — the <c>downstream-demo</c> launch profile sets it,
/// together with the rest of the configuration (see the sample's README).
/// </summary>
public static class DownstreamDemoEndpoints
{
    private const string BaseUrlKey = "DownstreamDemo:BaseUrl";

    /// <summary>
    /// The registrations — exactly what a real downstream looks like: a typed client, a base address, and the
    /// ONE authentication method that downstream accepts.
    /// </summary>
    public static IServiceCollection AddDownstreamDemo(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!IsEnabled(configuration, environment))
        {
            return services;
        }

        // One sample process hosts every downstream under its own path; in real life each has its own URL.
        var sample = new Uri(configuration[BaseUrlKey]!);
        Action<HttpClient> At(string path) => client => client.BaseAddress = new Uri(sample, $"{path}/");

        services.AddHttpClient<IBillingApi, BillingApi>("billing", At("billing")).AddOnBehalfOf();
        services.AddHttpClient<IReportsApi, ReportsApi>("reports", At("reports")).AddForwardedBearer();
        services.AddHttpClient<IInventoryApi, InventoryApi>("inventory", At("inventory")).AddClientCredentials();
        services.AddHttpClient<ILegacyApi, LegacyApi>("legacy", At("legacy")).AddBasicCredentials();
        services.AddHttpClient<IVendorApi, VendorApi>("vendor", At("vendor")).AddApiKeyCredentials();

        // The WRONG method on purpose: billing reached with the caller's own token instead of an exchanged
        // one. Billing refuses it (wrong audience) — the reason On-Behalf-Of exists. A plain named client, to
        // show that style too: resolved from IHttpClientFactory by name rather than injected as a type.
        services.AddHttpClient(WrongWayClient, At("billing")).AddForwardedBearer();

        return services;
    }

    public static IEndpointRouteBuilder MapDownstreamDemo(
        this WebApplication app, ApiVersionSet versionSet)
    {
        if (!IsEnabled(app.Configuration, app.Environment))
        {
            return app;
        }

        var demo = app.MapGroup("/demo")
            .WithTags("Demo")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(new ApiVersion(1, 0))
            .RequireAuthorization();

        demo.MapGet("/downstreams", async (
            IBillingApi billing,
            IReportsApi reports,
            IInventoryApi inventory,
            ILegacyApi legacy,
            IVendorApi vendor,
            IHttpClientFactory httpClientFactory,
            ClaimsPrincipal caller,
            CancellationToken cancellationToken) =>
        {
            // Sequential on purpose: the results read top to bottom in the same order as the registrations.
            List<Response.Call> calls =
            [
                await CallAsync("billing", "AddOnBehalfOf()", () => billing.WhoAmIAsync(cancellationToken)),
                await CallAsync("reports", "AddForwardedBearer()", () => reports.WhoAmIAsync(cancellationToken)),
                await CallAsync("inventory", "AddClientCredentials()", () => inventory.WhoAmIAsync(cancellationToken)),
                await CallAsync("legacy", "AddBasicCredentials()", () => legacy.WhoAmIAsync(cancellationToken)),
                await CallAsync("vendor", "AddApiKeyCredentials()", () => vendor.WhoAmIAsync(cancellationToken)),
                await CallAsync("billing", "AddForwardedBearer() — the wrong method for billing, expected to fail",
                    async () =>
                    {
                        // Read the response by hand to surface WHY billing refused: its WWW-Authenticate header.
                        using var response = await httpClientFactory.CreateClient(WrongWayClient)
                            .GetAsync("whoami", cancellationToken);
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new HttpRequestException(
                                $"{(int)response.StatusCode} {response.StatusCode}, " +
                                $"WWW-Authenticate: {response.Headers.WwwAuthenticate}",
                                inner: null,
                                response.StatusCode);
                        }

                        return (await response.Content.ReadFromJsonAsync<DownstreamIdentity>(cancellationToken))!;
                    }),
            ];

            return Results.Ok(new Response(
                caller.Identity?.Name, caller.Identity?.AuthenticationType, calls));
        })
        .WithName("CallEveryDownstream")
        .WithSummary("Calls each demo downstream once with its configured authentication method and reports " +
                     "what each one authorized. Try it as a Bearer user (all succeed) and with an X-Api-Key " +
                     "(the two per-user downstreams refuse).");

        return app;
    }

    private const string WrongWayClient = "billing-forwarded";

    private static bool IsEnabled(IConfiguration configuration, IHostEnvironment environment) =>
        environment.IsDevelopment() && !string.IsNullOrWhiteSpace(configuration[BaseUrlKey]);

    /// <summary>
    /// One call, with its failure turned into a row instead of an error response — the point of the demo is
    /// to see every outcome side by side, including the refusals. Real code lets these propagate:
    /// GlobalExceptionHandler maps them to 401 / 502.
    /// </summary>
    private static async Task<Response.Call> CallAsync(
        string downstream, string method, Func<Task<DownstreamIdentity>> call)
    {
        try
        {
            return new Response.Call(downstream, method, Succeeded: true, await call(), Error: null);
        }
        catch (CallerTokenRequiredException ex)
        {
            return Failed($"Not sent: {ex.Message}");
        }
        catch (TokenExchangeException ex)
        {
            return Failed($"Not sent — the token request failed ({ex.Failure}): {ex.Message}");
        }
        catch (HttpRequestException ex) when (ex.StatusCode is not null)
        {
            return Failed($"Sent, and the downstream refused it: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            return Failed($"Could not reach the downstream — is the sample running? {ex.Message}");
        }

        Response.Call Failed(string error) => new(downstream, method, Succeeded: false, Downstream: null, error);
    }

    /// <summary>Who called this API, and what happened on each downstream call.</summary>
    internal sealed record Response(string? Caller, string? CallerAuthenticatedWith, IReadOnlyList<Response.Call> Calls)
    {
        internal sealed record Call(
            string Target, string Method, bool Succeeded, DownstreamIdentity? Downstream, string? Error);
    }
}
