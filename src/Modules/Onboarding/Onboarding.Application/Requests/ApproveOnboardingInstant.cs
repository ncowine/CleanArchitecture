using BuildingBlocks.Messaging;
using Equipment.Contracts;
using Onboarding.Application.Abstractions;
using Onboarding.Domain;

namespace Onboarding.Application.Requests;

/// <summary>
/// The "instant" saga engine: runs all three provisioning steps synchronously, in this one handler, and
/// compensates in reverse order the moment a step reports failure. Simple to read top-to-bottom, but has
/// no crash recovery — if the process dies mid-method, whatever had already succeeded (e.g. a reserved
/// piece of equipment) is left in that state with no record of an in-flight saga to resume or compensate
/// it. <see cref="ApproveOnboardingStandard"/> is the version that survives a restart.
/// <para>
/// Each step call is cross-module (Equipment) or cross-service (licence/access) and commits on its own —
/// there is no distributed transaction spanning them and the OnboardingDbContext update below. That's the
/// whole point of the exercise: correctness comes from explicit compensation, not a database transaction
/// that can't span multiple databases anyway.
/// </para>
/// </summary>
public static class ApproveOnboardingInstant
{
    public sealed record Command(Guid OnboardingRequestId) : IRequest<Result>, IOnboardingCommand, IAuditableRequest
    {
        public string AuditResource => $"OnboardingRequest/{OnboardingRequestId}";
    }

    public sealed record Result(bool Ready, string? FailureReason);

    public sealed class Handler : IRequestHandler<Command, Result>
    {
        private readonly IOnboardingRequestRepository _requests;
        private readonly IEquipmentReservationService _equipment;
        private readonly ILicenceAllocationService _licences;
        private readonly IAccessProvisioningService _access;

        public Handler(
            IOnboardingRequestRepository requests,
            IEquipmentReservationService equipment,
            ILicenceAllocationService licences,
            IAccessProvisioningService access)
        {
            _requests = requests;
            _equipment = equipment;
            _licences = licences;
            _access = access;
        }

        public async Task<Result> Handle(Command command, CancellationToken cancellationToken)
        {
            var request = await _requests.GetAsync(command.OnboardingRequestId, cancellationToken)
                ?? throw new DomainException($"No onboarding request exists with id '{command.OnboardingRequestId}'.");

            request.Approve();

            // Step 1: reserve equipment.
            var equipmentResult = await _equipment.ReserveAsync(
                request.Id, request.RequiredEquipmentCategory, cancellationToken);
            if (!equipmentResult.Reserved)
            {
                request.RecordEquipmentFailed();
                request.MarkFailed(equipmentResult.Reason!);
                return new Result(false, equipmentResult.Reason);
            }
            request.RecordEquipmentReserved(equipmentResult.EquipmentId!.Value);

            // Step 2: allocate a software licence.
            var licenceResult = await _licences.AllocateAsync(request.Id, request.RequiredLicenceType, cancellationToken);
            if (!licenceResult.Allocated)
            {
                request.RecordLicenceFailed();
                await _equipment.ReleaseAsync(request.Id, cancellationToken);
                request.RecordEquipmentCompensated();
                request.MarkFailed(licenceResult.Reason!);
                return new Result(false, licenceResult.Reason);
            }
            request.RecordLicenceAllocated(licenceResult.LicenceId!.Value);

            // Step 3: provision system access.
            var accessResult = await _access.ProvisionAsync(request.Id, request.RequiredAccessLevel, cancellationToken);
            if (!accessResult.Provisioned)
            {
                request.RecordAccessFailed();
                await _licences.ReleaseAsync(request.Id, cancellationToken);
                request.RecordLicenceCompensated();
                await _equipment.ReleaseAsync(request.Id, cancellationToken);
                request.RecordEquipmentCompensated();
                request.MarkFailed(accessResult.Reason!);
                return new Result(false, accessResult.Reason);
            }
            request.RecordAccessProvisioned(accessResult.AccessId!.Value);

            request.MarkReady();
            return new Result(true, null);
        }
    }
}
