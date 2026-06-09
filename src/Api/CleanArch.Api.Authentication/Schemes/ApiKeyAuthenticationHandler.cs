using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CleanArch.Api.Authentication;

/// <summary>Authenticates a request by an <c>X-Api-Key</c> header (for service-to-service callers).</summary>
internal sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = AuthenticationSchemes.ApiKey;
    public const string HeaderName = AuthenticationSchemes.ApiKeyHeaderName;

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

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values) || string.IsNullOrWhiteSpace(values))
        {
            return AuthenticateResult.NoResult();
        }

        var result = await _validator.ValidateAsync(values.ToString(), Context.RequestAborted);
        if (result is null)
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var identity = new ClaimsIdentity(SchemeName);
        identity.AddClaim(new Claim(ClaimTypes.Name, result.Subject));
        foreach (var role in result.Roles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }
}
