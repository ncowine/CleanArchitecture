using Equipment.Contracts;
using Onboarding.Application.Abstractions;
using Onboarding.Domain;

namespace CleanArch.UnitTests;

internal sealed class FakeOnboardingRequestRepository : IOnboardingRequestRepository
{
    private readonly Dictionary<Guid, OnboardingRequest> _requests = new();

    public List<OnboardingRequest> Added { get; } = new();

    public void Seed(OnboardingRequest request) => _requests[request.Id] = request;

    public Task AddAsync(OnboardingRequest request, CancellationToken cancellationToken)
    {
        Added.Add(request);
        _requests[request.Id] = request;
        return Task.CompletedTask;
    }

    public Task<OnboardingRequest?> GetAsync(Guid onboardingRequestId, CancellationToken cancellationToken) =>
        Task.FromResult(_requests.TryGetValue(onboardingRequestId, out var request) ? request : null);
}

/// <summary>Stands in for the Equipment module's published contract — always returns a fixed result.</summary>
internal sealed class FakeEquipmentReservationService : IEquipmentReservationService
{
    private readonly EquipmentReservationResult _result;

    public List<Guid> ReserveCalls { get; } = new();
    public List<Guid> ReleaseCalls { get; } = new();

    private FakeEquipmentReservationService(EquipmentReservationResult result) => _result = result;

    public static FakeEquipmentReservationService Succeeding(Guid? equipmentId = null) =>
        new(new EquipmentReservationResult(true, equipmentId ?? Guid.NewGuid(), null));

    public static FakeEquipmentReservationService Failing(string reason) =>
        new(new EquipmentReservationResult(false, null, reason));

    public Task<EquipmentReservationResult> ReserveAsync(
        Guid onboardingRequestId, string category, CancellationToken cancellationToken)
    {
        ReserveCalls.Add(onboardingRequestId);
        return Task.FromResult(_result);
    }

    public Task ReleaseAsync(Guid onboardingRequestId, CancellationToken cancellationToken)
    {
        ReleaseCalls.Add(onboardingRequestId);
        return Task.CompletedTask;
    }
}

internal sealed class FakeLicenceAllocationService : ILicenceAllocationService
{
    private readonly LicenceAllocationResult _result;

    public List<Guid> AllocateCalls { get; } = new();
    public List<Guid> ReleaseCalls { get; } = new();

    private FakeLicenceAllocationService(LicenceAllocationResult result) => _result = result;

    public static FakeLicenceAllocationService Succeeding(Guid? licenceId = null) =>
        new(new LicenceAllocationResult(true, licenceId ?? Guid.NewGuid(), null));

    public static FakeLicenceAllocationService Failing(string reason) =>
        new(new LicenceAllocationResult(false, null, reason));

    public Task<LicenceAllocationResult> AllocateAsync(
        Guid onboardingRequestId, string licenceType, CancellationToken cancellationToken)
    {
        AllocateCalls.Add(onboardingRequestId);
        return Task.FromResult(_result);
    }

    public Task ReleaseAsync(Guid onboardingRequestId, CancellationToken cancellationToken)
    {
        ReleaseCalls.Add(onboardingRequestId);
        return Task.CompletedTask;
    }
}

internal sealed class FakeAccessProvisioningService : IAccessProvisioningService
{
    private readonly AccessProvisioningResult _result;

    public List<Guid> ProvisionCalls { get; } = new();
    public List<Guid> RevokeCalls { get; } = new();

    private FakeAccessProvisioningService(AccessProvisioningResult result) => _result = result;

    public static FakeAccessProvisioningService Succeeding(Guid? accessId = null) =>
        new(new AccessProvisioningResult(true, accessId ?? Guid.NewGuid(), null));

    public static FakeAccessProvisioningService Failing(string reason) =>
        new(new AccessProvisioningResult(false, null, reason));

    public Task<AccessProvisioningResult> ProvisionAsync(
        Guid onboardingRequestId, string accessLevel, CancellationToken cancellationToken)
    {
        ProvisionCalls.Add(onboardingRequestId);
        return Task.FromResult(_result);
    }

    public Task RevokeAsync(Guid onboardingRequestId, CancellationToken cancellationToken)
    {
        RevokeCalls.Add(onboardingRequestId);
        return Task.CompletedTask;
    }
}
