using System.DirectoryServices.AccountManagement;
using System.Runtime.Versioning;

namespace CleanArch.Api.Authentication;

/// <summary>
/// Builds the <see cref="PrincipalContext"/> both AD components bind through, from
/// <see cref="ActiveDirectoryOptions"/>. The caller owns disposal. Windows-only.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ActiveDirectoryContextFactory
{
    public static PrincipalContext Create(ActiveDirectoryOptions options) =>
        string.IsNullOrWhiteSpace(options.Server)
            ? new PrincipalContext(ContextType.Domain)
            : string.IsNullOrWhiteSpace(options.Container)
                ? new PrincipalContext(ContextType.Domain, options.Server)
                : new PrincipalContext(ContextType.Domain, options.Server, options.Container);
}
