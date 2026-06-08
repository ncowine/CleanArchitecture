using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CleanArch.Api.Authentication;

/// <summary>Authenticates a request by an <c>X-Api-Key</c> header (for service-to-service callers).</summary>
internal sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    private readonly IApiKeyValidator _validator;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiKeyValidator validator)
        : base(options, logger, encoder)
    {
        _validator = validator;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values) || string.IsNullOrWhiteSpace(values))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = _validator.Validate(values.ToString());
        if (identity is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
