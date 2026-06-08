using System.DirectoryServices.AccountManagement;
using System.Runtime.Versioning;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace CleanArch.Api.Authentication;

/// <summary>
/// Real Active Directory directory lookup: resolves a user's display name and group memberships (→ roles)
/// via an LDAP query. Windows-only (System.DirectoryServices.AccountManagement). This is the authorization
/// half — the credential check lives in <see cref="ActiveDirectoryCredentialValidator"/>. Wrapped by
/// <see cref="CachingUserDirectory"/> because a directory hit per request (the claims transformation can
/// run more than once per request) is expensive.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class ActiveDirectoryUserDirectory : IUserDirectory
{
    private readonly ActiveDirectoryOptions _options;
    private readonly ILogger<ActiveDirectoryUserDirectory> _logger;

    public ActiveDirectoryUserDirectory(
        IOptions<ActiveDirectoryOptions> options,
        ILogger<ActiveDirectoryUserDirectory> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<DirectoryUser?> FindAsync(string principalName, CancellationToken cancellationToken)
    {
        DirectoryUser? user = null;

        try
        {
            using var context = ActiveDirectoryContextFactory.Create(_options);
            using var principal = UserPrincipal.FindByIdentity(context, principalName);

            if (principal is not null)
            {
                user = new DirectoryUser(
                    principal.UserPrincipalName ?? principalName,
                    principal.DisplayName ?? principalName,
                    ReadGroupNames(principal));
            }
        }
        catch (PrincipalServerDownException ex)
        {
            // AD unreachable: treat as "no enrichment" (the principal keeps whatever auth gave it) rather
            // than failing the request. Logged so the cause is visible.
            DirectoryUnreachable(_logger, ex, principalName);
        }

        return Task.FromResult(user);
    }

    private static List<string> ReadGroupNames(UserPrincipal principal)
    {
        // GetGroups() = the user's DIRECT group memberships. Names must be materialized before the context
        // and group principals are disposed (the result is lazily enumerated). Use GetAuthorizationGroups()
        // instead if you need the transitive set (nested groups + primary group).
        using var groups = principal.GetGroups();

        var names = new List<string>();
        foreach (var group in groups)
        {
            using (group)
            {
                if (!string.IsNullOrEmpty(group.SamAccountName))
                {
                    names.Add(group.SamAccountName);
                }
            }
        }

        return names;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Active Directory is unreachable; cannot resolve user '{PrincipalName}'.")]
    private static partial void DirectoryUnreachable(ILogger logger, Exception exception, string principalName);
}

/// <summary>
/// Caching decorator over an <see cref="IUserDirectory"/>. Mirrors the Students module's
/// <c>CachingStudentDirectory</c>: <see cref="HybridCache.GetOrCreateAsync"/> collapses concurrent misses
/// for the same user into one lookup (stampede protection) and uses the configured short TTL. In-memory
/// today; Redis-ready with no change here.
/// </summary>
internal sealed class CachingUserDirectory : IUserDirectory
{
    private readonly IUserDirectory _inner;
    private readonly HybridCache _cache;

    public CachingUserDirectory(IUserDirectory inner, HybridCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public Task<DirectoryUser?> FindAsync(string principalName, CancellationToken cancellationToken) =>
        _cache.GetOrCreateAsync(
            $"ad:user:{principalName}",
            (inner: _inner, principalName),
            static (state, ct) => new ValueTask<DirectoryUser?>(state.inner.FindAsync(state.principalName, ct)),
            cancellationToken: cancellationToken).AsTask();
}
