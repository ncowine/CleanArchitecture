using System.DirectoryServices.AccountManagement;
using System.Runtime.Versioning;
using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace CleanArch.Api.Authentication;

public interface ICredentialValidator
{
    /// <summary>Returns the identity for valid credentials, or null if the username/password are rejected.</summary>
    Task<ClaimsIdentity?> ValidateAsync(string username, string password, CancellationToken cancellationToken);
}

/// <summary>Binding options for Active Directory. A blank <see cref="Server"/> uses the machine's joined domain.</summary>
public sealed class ActiveDirectoryOptions
{
    public const string SectionName = "ActiveDirectory";

    /// <summary>Domain name or domain-controller host. Null/blank ⇒ the host machine's current domain.</summary>
    public string? Server { get; set; }

    /// <summary>Optional base container (distinguished name) to scope the bind, e.g. "DC=corp,DC=example,DC=com".</summary>
    public string? Container { get; set; }
}

/// <summary>
/// Real Active Directory credential check. <see cref="PrincipalContext.ValidateCredentials(string, string, ContextOptions)"/>
/// performs an LDAP bind against the domain controller — AD itself verifies the password; it is never seen
/// or stored here. Windows-only (System.DirectoryServices.AccountManagement). Authorization (roles from
/// group membership) is a separate step handled by <see cref="ActiveDirectoryClaimsTransformation"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class ActiveDirectoryCredentialValidator : ICredentialValidator
{
    private readonly ActiveDirectoryOptions _options;
    private readonly ILogger<ActiveDirectoryCredentialValidator> _logger;

    public ActiveDirectoryCredentialValidator(
        IOptions<ActiveDirectoryOptions> options,
        ILogger<ActiveDirectoryCredentialValidator> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<ClaimsIdentity?> ValidateAsync(string username, string password, CancellationToken cancellationToken)
    {
        ClaimsIdentity? identity = null;

        try
        {
            using var context = ActiveDirectoryContextFactory.Create(_options);

            // A wrong password returns false (no exception). ContextOptions.Negotiate = Kerberos/NTLM.
            if (context.ValidateCredentials(username, password, ContextOptions.Negotiate))
            {
                identity = new ClaimsIdentity(BasicAuthenticationHandler.SchemeName);
                identity.AddClaim(new Claim(ClaimTypes.Name, username));
            }
        }
        catch (PrincipalServerDownException ex)
        {
            // AD unreachable: log and treat as not-validated (the handler turns null into a 401) rather
            // than throwing a 500. A real deployment might prefer 503 here.
            AdUnreachable(_logger, ex);
        }

        return Task.FromResult(identity);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Active Directory is unreachable; cannot validate credentials.")]
    private static partial void AdUnreachable(ILogger logger, Exception exception);
}
