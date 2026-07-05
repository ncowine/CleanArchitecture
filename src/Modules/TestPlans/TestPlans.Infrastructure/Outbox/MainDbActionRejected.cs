namespace TestPlans.Infrastructure.Outbox;

/// <summary>
/// Integration event published by the primary system when it refuses a synced action (the referenced task,
/// version, or platform no longer exists). Delivered back to the Tester Guide module (the sync saga's
/// reverse leg), where it flags the originating guide action as rejected. Internal to Test Plans — the
/// dispatcher maps it onto the Guide's published contract. <see cref="SourceReference"/> is the guide
/// action id the caller supplied.
/// </summary>
public sealed record MainDbActionRejected(Guid SourceReference, string Reason);
