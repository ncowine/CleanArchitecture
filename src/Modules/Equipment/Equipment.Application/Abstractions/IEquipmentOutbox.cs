using BuildingBlocks.Outbox;

namespace Equipment.Application.Abstractions;

/// <summary>
/// The Equipment module's outbox: stages an integration event (from <c>Equipment.Messages</c>) in the SAME transaction
/// as the change, to be sent to other applications once that transaction commits.
/// </summary>
/// <remarks>
/// <para>
/// A module-specific interface on purpose, not the shared <see cref="IOutbox"/>: <see cref="IOutbox"/> is registered
/// non-keyed, and Onboarding already owns it. A second registration would silently win, and one module's events would
/// be written into the other module's table (tutorials/60-talking-across-modules.md §16).
/// </para>
/// <para>
/// The handler only says "this happened". Whether it reaches RabbitMQ — and how — is decided by the host
/// (docs/messaging/adr/0003). When messaging isn't configured, the registered implementation records nothing.
/// </para>
/// </remarks>
public interface IEquipmentOutbox : IOutbox;
