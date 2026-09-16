namespace Onboarding.Application.Outbox;

/// <summary>
/// The Standard saga's step vocabulary — each message names one action to take against the onboarding
/// request it carries. Every message is idempotent by design: the dispatcher re-derives everything it
/// needs from the request/saga rows rather than trusting stale data carried in the message itself, so a
/// redelivery (at-least-once, per the outbox contract) is always safe to run again.
/// </summary>
public sealed record ReserveEquipmentForOnboarding(Guid OnboardingRequestId);

public sealed record AllocateLicenceForOnboarding(Guid OnboardingRequestId);

public sealed record ProvisionAccessForOnboarding(Guid OnboardingRequestId);

/// <summary>Compensation: release the licence allocated in the forward leg.</summary>
public sealed record ReleaseLicenceForOnboarding(Guid OnboardingRequestId);

/// <summary>Compensation: release the equipment reserved in the forward leg.</summary>
public sealed record ReleaseEquipmentForOnboarding(Guid OnboardingRequestId);
