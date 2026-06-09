using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CleanArch.Api.Authentication;

/// <summary>
/// Authenticates a request by HTTP Basic credentials (<c>Authorization: Basic base64(user:password)</c>),
/// validated against Active Directory via <see cref="ICredentialValidator"/>. For interactive/human
/// callers; service callers use the API key instead. Basic credentials are base64, NOT encrypted — only
/// acceptable over HTTPS.
/// </summary>
internal sealed class BasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = AuthenticationSchemes.Basic;

    private readonly ICredentialValidator _validator;

    public BasicAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ICredentialValidator validator)
        : base(options, logger, encoder)
    {
        _validator = validator;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var headerValues))
        {
            return AuthenticateResult.NoResult();
        }

        if (!AuthenticationHeaderValue.TryParse(headerValues.ToString(), out var header) ||
            !"Basic".Equals(header.Scheme, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(header.Parameter))
        {
            // Not a Basic header (could be another scheme) — let the pipeline decide.
            return AuthenticateResult.NoResult();
        }

        string username;
        string password;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
            var separator = decoded.IndexOf(':');
            if (separator < 0)
            {
                return AuthenticateResult.Fail("Malformed Basic credentials (expected 'user:password').");
            }

            username = decoded[..separator];
            password = decoded[(separator + 1)..];
        }
        catch (FormatException)
        {
            return AuthenticateResult.Fail("Basic credentials were not valid base64.");
        }

        var identity = await _validator.ValidateAsync(username, password, Context.RequestAborted);
        if (identity is null)
        {
            return AuthenticateResult.Fail("Invalid username or password.");
        }

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // Advertise Basic so clients (and browsers) know how to authenticate on a 401.
        Response.Headers.WWWAuthenticate = "Basic realm=\"CleanArchitecture API\", charset=\"UTF-8\"";
        return base.HandleChallengeAsync(properties);
    }
}
