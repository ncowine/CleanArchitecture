using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;

namespace CleanArch.Api;

/// <summary>
/// Translates expected exceptions into RFC 7807 problem responses. Validation failures from the
/// pipeline become a 400 with per-field errors; domain rule violations become a plain 400.
/// Anything else falls through to the framework's default 500 handling.
/// </summary>
internal sealed class GlobalExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        switch (exception)
        {
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

            // Each module owns its own DomainException type; both map to the same 400 response. A
            // shared base in a dependency-free kernel would collapse these cases as more modules land.
            case Students.Domain.DomainException:
            case Library.Domain.DomainException:
                await Results
                    .Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest, title: "Bad request")
                    .ExecuteAsync(httpContext);
                return true;

            default:
                return false;
        }
    }
}
