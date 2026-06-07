using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Outbox;

/// <summary>
/// Module-specific delivery logic: maps an outbox message (by its <paramref name="type"/> discriminator
/// and JSON <paramref name="content"/>) onto the right call into another module's published contract.
/// One implementation per module, keyed by the module's <typeparamref name="TContext"/> so multiple
/// modules can coexist in a single host without their dispatchers colliding.
/// </summary>
public interface IOutboxDispatcher<TContext> where TContext : DbContext
{
    Task DispatchAsync(Guid messageId, string type, string content, CancellationToken cancellationToken);
}
