using System.Text.Json;
using BuildingBlocks.Outbox;
using Equipment.Contracts;
using Microsoft.EntityFrameworkCore;
using Onboarding.Application.Abstractions;
using Onboarding.Application.Outbox;
using Onboarding.Domain;
using Onboarding.Infrastructure.Persistence;

namespace Onboarding.Infrastructure.Outbox;

/// <summary>
/// The Standard saga's step engine. Each call handles exactly one step for one onboarding request,
/// re-loading the request and saga state fresh rather than trusting anything beyond the id carried in the
/// message — that's what makes a redelivery (the outbox's at-least-once guarantee) safe to run again.
/// <para>
/// <b>Business failure vs. unexpected exception</b>: a step reporting "no equipment available" or
/// "unsupported access level" is caught right here and handled by recording the failure and enqueueing
/// compensation — it must never throw, or the generic <see cref="OutboxProcessor{TContext}"/> would treat
/// an entirely ordinary business outcome as a delivery failure and retry/dead-letter it. Only a genuinely
/// unexpected condition (the request or saga row is missing — a data-integrity bug, not a business
/// outcome) throws, which is the correct case for the outbox's normal retry-then-dead-letter path.
/// </para>
/// </summary>
internal sealed class OnboardingOutboxDispatcher : IOutboxDispatcher<OnboardingDbContext>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly OnboardingDbContext _db;
    private readonly IEquipmentReservationService _equipment;
    private readonly ILicenceAllocationService _licences;
    private readonly IAccessProvisioningService _access;
    private readonly IOutbox _outbox;

    public OnboardingOutboxDispatcher(
        OnboardingDbContext db,
        IEquipmentReservationService equipment,
        ILicenceAllocationService licences,
        IAccessProvisioningService access,
        IOutbox outbox)
    {
        _db = db;
        _equipment = equipment;
        _licences = licences;
        _access = access;
        _outbox = outbox;
    }

    public Task DispatchAsync(Guid messageId, string type, string content, CancellationToken cancellationToken)
    {
        var onboardingRequestId = type switch
        {
            nameof(ReserveEquipmentForOnboarding) => Deserialize<ReserveEquipmentForOnboarding>(content, messageId).OnboardingRequestId,
            nameof(AllocateLicenceForOnboarding) => Deserialize<AllocateLicenceForOnboarding>(content, messageId).OnboardingRequestId,
            nameof(ProvisionAccessForOnboarding) => Deserialize<ProvisionAccessForOnboarding>(content, messageId).OnboardingRequestId,
            nameof(ReleaseLicenceForOnboarding) => Deserialize<ReleaseLicenceForOnboarding>(content, messageId).OnboardingRequestId,
            nameof(ReleaseEquipmentForOnboarding) => Deserialize<ReleaseEquipmentForOnboarding>(content, messageId).OnboardingRequestId,
            _ => throw new InvalidOperationException($"Unknown outbox message type '{type}'."),
        };

        return type switch
        {
            nameof(ReserveEquipmentForOnboarding) => HandleReserveEquipmentAsync(onboardingRequestId, cancellationToken),
            nameof(AllocateLicenceForOnboarding) => HandleAllocateLicenceAsync(onboardingRequestId, cancellationToken),
            nameof(ProvisionAccessForOnboarding) => HandleProvisionAccessAsync(onboardingRequestId, cancellationToken),
            nameof(ReleaseLicenceForOnboarding) => HandleReleaseLicenceAsync(onboardingRequestId, cancellationToken),
            nameof(ReleaseEquipmentForOnboarding) => HandleReleaseEquipmentAsync(onboardingRequestId, cancellationToken),
            _ => throw new InvalidOperationException($"Unknown outbox message type '{type}'."),
        };
    }

    private async Task HandleReserveEquipmentAsync(Guid onboardingRequestId, CancellationToken cancellationToken)
    {
        var (request, saga) = await LoadAsync(onboardingRequestId, cancellationToken);

        var result = await _equipment.ReserveAsync(onboardingRequestId, request.RequiredEquipmentCategory, cancellationToken);
        if (!result.Reserved)
        {
            request.RecordEquipmentFailed();
            request.MarkFailed(result.Reason!);
            saga.MarkFailed(); // first step — nothing to compensate
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        request.RecordEquipmentReserved(result.EquipmentId!.Value);
        saga.AdvanceTo(SagaStep.AllocateLicence);
        Enqueue(new AllocateLicenceForOnboarding(onboardingRequestId));
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task HandleAllocateLicenceAsync(Guid onboardingRequestId, CancellationToken cancellationToken)
    {
        var (request, saga) = await LoadAsync(onboardingRequestId, cancellationToken);

        var result = await _licences.AllocateAsync(onboardingRequestId, request.RequiredLicenceType, cancellationToken);
        if (!result.Allocated)
        {
            request.RecordLicenceFailed();
            request.MarkFailed(result.Reason!);
            saga.BeginCompensating(SagaStep.ReserveEquipment);
            Enqueue(new ReleaseEquipmentForOnboarding(onboardingRequestId));
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        request.RecordLicenceAllocated(result.LicenceId!.Value);
        saga.AdvanceTo(SagaStep.ProvisionAccess);
        Enqueue(new ProvisionAccessForOnboarding(onboardingRequestId));
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task HandleProvisionAccessAsync(Guid onboardingRequestId, CancellationToken cancellationToken)
    {
        var (request, saga) = await LoadAsync(onboardingRequestId, cancellationToken);

        var result = await _access.ProvisionAsync(onboardingRequestId, request.RequiredAccessLevel, cancellationToken);
        if (!result.Provisioned)
        {
            request.RecordAccessFailed();
            request.MarkFailed(result.Reason!);
            saga.BeginCompensating(SagaStep.AllocateLicence);
            Enqueue(new ReleaseLicenceForOnboarding(onboardingRequestId));
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        request.RecordAccessProvisioned(result.AccessId!.Value);
        request.MarkReady();
        saga.Complete();
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task HandleReleaseLicenceAsync(Guid onboardingRequestId, CancellationToken cancellationToken)
    {
        var (request, saga) = await LoadAsync(onboardingRequestId, cancellationToken);

        await _licences.ReleaseAsync(onboardingRequestId, cancellationToken);
        request.RecordLicenceCompensated();
        saga.BeginCompensating(SagaStep.ReserveEquipment);
        Enqueue(new ReleaseEquipmentForOnboarding(onboardingRequestId));
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task HandleReleaseEquipmentAsync(Guid onboardingRequestId, CancellationToken cancellationToken)
    {
        var (request, saga) = await LoadAsync(onboardingRequestId, cancellationToken);

        await _equipment.ReleaseAsync(onboardingRequestId, cancellationToken);
        request.RecordEquipmentCompensated();
        saga.MarkCompensated();
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<(OnboardingRequest Request, OnboardingSagaState Saga)> LoadAsync(
        Guid onboardingRequestId, CancellationToken cancellationToken)
    {
        var request = await _db.Requests.FirstOrDefaultAsync(r => r.Id == onboardingRequestId, cancellationToken)
            ?? throw new InvalidOperationException($"Onboarding request '{onboardingRequestId}' does not exist.");

        var saga = await _db.SagaStates.FirstOrDefaultAsync(s => s.OnboardingRequestId == onboardingRequestId, cancellationToken)
            ?? throw new InvalidOperationException($"No saga state exists for onboarding request '{onboardingRequestId}'.");

        return (request, saga);
    }

    /// <summary>
    /// Enqueues the next step in the SAME transaction the current step's changes will be saved in — this
    /// dispatcher and the injected <see cref="IOutbox"/> both resolve the same scoped DbContext (the one
    /// the outbox processor's batch is about to commit), so the next step is reliably queued if and only
    /// if this step's own changes commit.
    /// </summary>
    private void Enqueue<TEvent>(TEvent message) where TEvent : class => _outbox.Enqueue(message);

    private static TMessage Deserialize<TMessage>(string content, Guid messageId) =>
        JsonSerializer.Deserialize<TMessage>(content, JsonOptions)
            ?? throw new InvalidOperationException($"Outbox message {messageId} had empty content.");
}
