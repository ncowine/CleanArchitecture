using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Outbox.Messaging;

/// <summary>
/// The message classes <typeparamref name="TContext"/>'s outbox sends to RabbitMQ, found by the outbox row's type name
/// (<c>typeof(TEvent).Name</c>, as <see cref="IOutbox.Enqueue{TEvent}"/> writes it).
/// </summary>
public sealed class OutboxMessageTypes<TContext> where TContext : DbContext
{
    private readonly Dictionary<string, Type> _byName;

    public OutboxMessageTypes(IEnumerable<Type> messageTypes)
    {
        ArgumentNullException.ThrowIfNull(messageTypes);

        var types = messageTypes.Distinct().ToList();
        var clashes = types.GroupBy(type => type.Name, StringComparer.Ordinal).Where(group => group.Count() > 1).ToList();
        if (clashes.Count > 0)
        {
            // The outbox stores only the short type name, so two classes with the same name can't be told apart.
            throw new ArgumentException(
                $"Outbox message types for {typeof(TContext).Name} must have distinct names: " +
                string.Join("; ", clashes.Select(group => string.Join(", ", group.Select(type => type.FullName)))),
                nameof(messageTypes));
        }

        _byName = types.ToDictionary(type => type.Name, StringComparer.Ordinal);
    }

    public bool TryGet(string typeName, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Type? messageType) =>
        _byName.TryGetValue(typeName, out messageType);
}
