using BuildingBlocks.Auditing;
using BuildingBlocks.Messaging;
using FluentValidation;
using TestPlans.Application.Abstractions;
using TestPlans.Domain;

namespace TestPlans.Application.Results;

/// <summary>
/// Record a result against a task natively in the primary system (Source = Primary): upserts the current
/// status for the (task, platform, version) and appends to the action log. The actor is the current
/// principal. This is the primary's own action path; the Guide's synced actions land via
/// <c>ITestPlanActionLog</c> instead.
/// </summary>
public static class RecordResult
{
    public sealed record Command(Guid TestTaskId, Guid PlatformId, Guid TestPlanVersionId, TaskResultStatus Status)
        : IRequest<Guid>, ITestPlansCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.TestTaskId).NotEmpty();
            RuleFor(command => command.PlatformId).NotEmpty();
            RuleFor(command => command.TestPlanVersionId).NotEmpty();
            RuleFor(command => command.Status).IsInEnum();
        }
    }

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly ITestContentRepository _content;
        private readonly IPlatformRepository _platforms;
        private readonly ITestPlanRepository _plans;
        private readonly ITaskResultStore _results;
        private readonly ICurrentActor _actor;

        public Handler(
            ITestContentRepository content,
            IPlatformRepository platforms,
            ITestPlanRepository plans,
            ITaskResultStore results,
            ICurrentActor actor)
        {
            _content = content;
            _platforms = platforms;
            _plans = plans;
            _results = results;
            _actor = actor;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            if (!await _content.TaskExistsAsync(command.TestTaskId, cancellationToken))
                throw new DomainException($"No task exists with id '{command.TestTaskId}'.");
            if (!await _platforms.ExistsAsync(command.PlatformId, cancellationToken))
                throw new DomainException($"No platform exists with id '{command.PlatformId}'.");
            if (!await _plans.VersionExistsAsync(command.TestPlanVersionId, cancellationToken))
                throw new DomainException($"No version exists with id '{command.TestPlanVersionId}'.");

            var actionId = Guid.NewGuid();
            await _results.RecordAsync(
                actionId,
                command.TestTaskId,
                command.PlatformId,
                command.TestPlanVersionId,
                command.Status,
                _actor.Current,
                ActionSource.Primary,
                DateTime.UtcNow,
                cancellationToken);

            return actionId;
        }
    }
}
