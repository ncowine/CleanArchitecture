namespace CleanArch.Api.Authentication;

public sealed record DirectoryUser(string PrincipalName, string DisplayName, IReadOnlyList<string> Roles);

public interface IUserDirectory
{
    Task<DirectoryUser?> FindAsync(string principalName, CancellationToken cancellationToken);
}

/// <summary>
/// FAKE Active Directory. In production, implement this against on-prem AD via LDAP
/// (System.DirectoryServices) or Entra ID via Microsoft Graph — behind this same interface — and cache
/// the results (a directory lookup per request is expensive; this is a textbook HybridCache candidate).
/// </summary>
internal sealed class FakeUserDirectory : IUserDirectory
{
    public Task<DirectoryUser?> FindAsync(string principalName, CancellationToken cancellationToken)
    {
        // Pretend AD resolved the user and their group memberships → roles.
        var roles = principalName.Contains("admin", StringComparison.OrdinalIgnoreCase)
            ? new[] { "registrar", "admin" }
            : new[] { "registrar" };

        return Task.FromResult<DirectoryUser?>(new DirectoryUser(principalName, principalName, roles));
    }
}
