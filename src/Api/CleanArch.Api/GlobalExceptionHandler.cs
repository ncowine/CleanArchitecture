using CleanArch.Api.Authentication;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;

namespace CleanArch.Api;

/// <summary>
/// Translates expected exceptions into RFC 7807 problem responses. Validation failures from the
/// pipeline become a 400 with per-field errors; domain rule violations become a plain 400; a failed
/// On-Behalf-Of token exchange becomes a 401 (the caller's token was rejected) or 502 (the provider was
/// unavailable); a client disconnect becomes a 499. Anything else falls through to the framework's
/// default 500 handling.
/// </summary>
internal sealed class GlobalExceptionHandler : IExceptionHandler
{
    // Not a registered .NET status code — it's nginx's long-standing convention for "the client closed
    // the connection before we could respond" — but it's the right number here for the same reason it
    // caught on there: it keeps this case out of every 5xx-based alert and dashboard without inventing a
    // second, incompatible convention of our own.
    private const int ClientClosedRequest = 499;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        switch (exception)
        {
            // The caller went away mid-request (closed the tab, the connection dropped) — RequestAborted
            // is what actually fired. This is not a server failure: nothing was wrong with the handler,
            // there is just no one left to answer. Guarded on RequestAborted specifically so a future
            // server-imposed deadline (a request timeout we set) — which IS our failure to answer in
            // time — keeps falling through to the default 5xx below instead of being hidden here.
            case OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested:
                httpContext.Response.StatusCode = ClientClosedRequest;
                return true;

            case ValidationException validationException:
                var errors = validationException.Errors
                    .GroupBy(failure => failure.PropertyName)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(failure => failure.ErrorMessage).ToArray());

                await Results
                    .ValidationProblem(errors, title: "One or more validation errors occurred.")
                    .ExecuteAsync(httpContext);
                return true;

            // Each module owns its own DomainException type; all map to the same 400 response. A
            // shared base in a dependency-free kernel would collapse these cases as more modules land.
            case Equipment.Domain.DomainException:
            case Onboarding.Domain.DomainException:
                await Results
                    .Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest, title: "Bad request")
                    .ExecuteAsync(httpContext);
                return true;

            // A failed On-Behalf-Of exchange: a rejected subject token is the caller's problem (401);
            // an unreachable/erroring provider is a bad gateway (502). Never a 500.
            case TokenExchangeException tokenExchange:
                var (status, title) = tokenExchange.Failure == TokenExchangeFailure.SubjectRejected
                    ? (StatusCodes.Status401Unauthorized, "Unauthorized")
                    : (StatusCodes.Status502BadGateway, "Upstream identity provider unavailable");
                await Results
                    .Problem(
                        detail: tokenExchange.Message,
                        statusCode: status,
                        title: title,
                        // Which downstream failed, as a machine-readable field — with several configured,
                        // the response is otherwise indistinguishable between them.
                        extensions: new Dictionary<string, object?>
                        {
                            ["downstream"] = tokenExchange.DownstreamName,
                        })
                    .ExecuteAsync(httpContext);
                return true;

            default:
                return false;
        }
    }
}
