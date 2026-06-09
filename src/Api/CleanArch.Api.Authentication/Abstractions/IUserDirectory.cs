namespace CleanArch.Api.Authentication;

public sealed record DirectoryUser(string PrincipalName, string DisplayName, IReadOnlyList<string> Roles);

public interface IUserDirectory
{
    Task<DirectoryUser?> FindAsync(string principalName, CancellationToken cancellationToken);
}

/// <summary>
/// FAKE Active Directory — the non-Windows fallback. The real implementation is
/// <see cref="ActiveDirectoryUserDirectory"/> (LDAP via System.DirectoryServices), cached by
/// <see cref="CachingUserDirectory"/>; this stub keeps the app bootable off-Windows. An Entra ID / Microsoft
/// Graph implementation would slot in behind this same interface.
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
