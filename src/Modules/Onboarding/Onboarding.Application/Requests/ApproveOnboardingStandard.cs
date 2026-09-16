using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Onboarding.Application.Abstractions;
using Onboarding.Application.Outbox;
using Onboarding.Domain;

namespace Onboarding.Application.Requests;

/// <summary>
/// The "standard" saga engine: approves the request and enqueues the first step, then returns
/// immediately — the outbox processor drives the rest, one step per poll tick, each step recorded to the
/// database before the next is enqueued. Because progress lives in <see cref="OnboardingSagaState"/> and
/// the outbox table rather than in memory, a process restart mid-saga resumes exactly where it left off:
/// the next poll just finds the same undelivered message still sitting there. Compare
/// <see cref="ApproveOnboardingInstant"/>, which runs the same steps synchronously with no such recovery.
/// </summary>
public static class ApproveOnboardingStandard
{
    public sealed record Command(Guid OnboardingRequestId) : IRequest<Result>, IOnboardingCommand, IAuditableRequest
    {
        public string AuditResource => $"OnboardingRequest/{OnboardingRequestId}";
    }

    public sealed record Result(string SagaStatus);

    public sealed class Handler : IRequestHandler<Command, Result>
    {
        private readonly IOnboardingRequestRepository _requests;
        private readonly IOnboardingSagaStateRepository _sagaStates;
        private readonly IOutbox _outbox;

        public Handler(
            IOnboardingRequestRepository requests,
            IOnboardingSagaStateRepository sagaStates,
            IOutbox outbox)
        {
            _requests = requests;
            _sagaStates = sagaStates;
            _outbox = outbox;
        }

        public async Task<Result> Handle(Command command, CancellationToken cancellationToken)
        {
            var request = await _requests.GetAsync(command.OnboardingRequestId, cancellationToken)
                ?? throw new DomainException($"No onboarding request exists with id '{command.OnboardingRequestId}'.");

            request.Approve();

            var saga = OnboardingSagaState.Start(request.Id);
            await _sagaStates.AddAsync(saga, cancellationToken);

            // Atomic with the approval and the saga row above (same transaction): the first step is
            // reliably queued the moment this commits, even if the process dies immediately after.
            _outbox.Enqueue(new ReserveEquipmentForOnboarding(request.Id));

            return new Result(saga.Status.ToString());
        }
    }
}
