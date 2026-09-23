using System.Text.Json;

namespace Common.RabbitMQ;

/// <summary>
/// The wire format for every message this library sends or receives. <see cref="MessageType"/> is a
/// stable, short string — never a CLR type name. A net472 build and a net10.0 build of "the same" class
/// are two different physical assemblies with two different identities, so resolving a type from
/// assembly-qualified data on the wire (<c>Type.GetType(...)</c>, or a serializer's own type-name
/// handling) breaks across that boundary even when both sides compile the same source. Each process
/// instead maintains its own local <see cref="IMessageTypeRegistry"/> mapping this string to a type it
/// already knows about.
/// </summary>
public sealed record MessageEnvelope(
    Guid MessageId,
    string MessageType,
    int SchemaVersion,
    string? CorrelationId,
    DateTimeOffset OccurredOnUtc,
    JsonElement Payload);
