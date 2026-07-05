namespace TesterGuide.Domain;

/// <summary>
/// Where a guide action stands relative to the primary system's action log.
/// <list type="bullet">
/// <item><see cref="NotSynced"/> — the config has sync disabled; the action lives only in the tool.</item>
/// <item><see cref="Pending"/> — sync was requested (enqueued to the outbox); the forward leg has not run yet.</item>
/// <item><see cref="Synced"/> — the primary recorded it; the forward leg completed successfully.</item>
/// <item><see cref="Rejected"/> — the primary refused it (task/version gone) and compensated back.</item>
/// </list>
/// A <see cref="Pending"/> action reaches a terminal state once the forward leg runs: <see cref="Synced"/> on
/// acceptance, or <see cref="Rejected"/> when a compensation arrives. Persisted by name (not ordinal), so the
/// member order here is cosmetic.
/// </summary>
public enum SyncState
{
    NotSynced,
    Pending,
    Synced,
    Rejected,
}
