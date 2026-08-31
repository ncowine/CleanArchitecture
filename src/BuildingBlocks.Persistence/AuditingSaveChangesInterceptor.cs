using System.Globalization;
using BuildingBlocks.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BuildingBlocks.Persistence;

/// <summary>
/// Captures the before/after change-set of every write and hands it to the scoped <see cref="IAuditScope"/>,
/// so the audit record can show exactly which entity changed and how. Runs as part of SaveChanges (inside
/// the command's transaction), so only committed changes are reported. Sensitive property values are
/// redacted; the outbox table (infrastructure plumbing) is skipped.
/// </summary>
public sealed class AuditingSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IAuditScope _scope;

    public AuditingSaveChangesInterceptor(IAuditScope scope) => _scope = scope;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Capture(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            // The outbox is transactional plumbing, not business data the user edited — don't audit it.
            if (entry.Metadata.ClrType.Name == "OutboxMessage")
            {
                continue;
            }

            var operation = entry.State switch
            {
                EntityState.Added => ChangeOperation.Added,
                EntityState.Deleted => ChangeOperation.Deleted,
                _ => ChangeOperation.Modified,
            };

            var properties = new List<PropertyChange>();
            foreach (var property in entry.Properties)
            {
                if (property.Metadata.IsPrimaryKey())
                {
                    continue; // the key is carried on EntityChange itself
                }

                var sensitive = IsSensitive(property.Metadata.Name);
                switch (operation)
                {
                    case ChangeOperation.Added:
                        properties.Add(new PropertyChange(property.Metadata.Name, null, Format(property.CurrentValue, sensitive)));
                        break;
                    case ChangeOperation.Deleted:
                        properties.Add(new PropertyChange(property.Metadata.Name, Format(property.OriginalValue, sensitive), null));
                        break;
                    default:
                        if (property.IsModified)
                        {
                            properties.Add(new PropertyChange(
                                property.Metadata.Name,
                                Format(property.OriginalValue, sensitive),
                                Format(property.CurrentValue, sensitive)));
                        }

                        break;
                }
            }

            _scope.Add(new EntityChange(entry.Metadata.ClrType.Name, PrimaryKey(entry), operation, properties));
        }
    }

    // The markers, the length limit and the redaction text are shared with every other route into the
    // trail (see AuditRedaction) — one policy, so a value is treated the same however it was captured.
    private static bool IsSensitive(string propertyName) => AuditRedaction.IsSensitive(propertyName);

    private static string PrimaryKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null)
        {
            return "(keyless)";
        }

        var parts = key.Properties.Select(property =>
        {
            var value = entry.Property(property.Name).CurrentValue ?? entry.Property(property.Name).OriginalValue;
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        });

        return string.Join(",", parts);
    }

    private static string? Format(object? value, bool sensitive)
    {
        if (value is null)
        {
            return null;
        }

        if (sensitive)
        {
            return AuditRedaction.RedactedValue;
        }

        return AuditRedaction.Truncate(Convert.ToString(value, CultureInfo.InvariantCulture) ?? value.ToString());
    }
}
