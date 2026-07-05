using BuildingBlocks.Auditing;
using BuildingBlocks.Messaging;
using BuildingBlocks.RealTime;
using FluentValidation;
using TesterGuide.Application.Abstractions;
using TesterGuide.Application.Outbox;
using TesterGuide.Domain;

namespace TesterGuide.Application.Actions;

/// <summary>
/// Record a tester's action against a task in a config. Writes the guide's own action-log entry and — when
/// the config has sync enabled — enqueues <see cref="MainDbActionRequested"/> in the same transaction, so
/// the action reliably mirrors into the primary system's action log (the sync saga's forward leg). The
/// version is taken from the config (it is dedicated to one). The task is not pre-validated against the
/// primary: the guide records optimistically and the async sync is the arbiter — a task/version that no
/// longer exists there comes back as a rejection (the reverse leg).
/// </summary>
public static class RecordAction
{
    public sealed record Command(Guid GuideConfigId, Guid TestTaskId, Guid PlatformId, ActionStatus Status)
        : IRequest<Result>, ITesterGuideCommand, IAuditableRequest;

    public sealed record Result(Guid GuideActionId, string SyncState);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.GuideConfigId).NotEmpty();
            RuleFor(command => command.TestTaskId).NotEmpty();
            RuleFor(command => command.PlatformId).NotEmpty();
            RuleFor(command => command.Status).IsInEnum();
        }
    }

    public sealed class Handler : IRequestHandler<Command, Result>
    {
        private readonly IGuideConfigRepository _configs;
        private readonly IGuideActionLogRepository _actions;
        private readonly ITesterGuideOutbox _outbox;
        private readonly IRealtimeDispatch _realtime;
        private readonly ICurrentActor _actor;

        public Handler(
            IGuideConfigRepository configs,
            IGuideActionLogRepository actions,
            ITesterGuideOutbox outbox,
            IRealtimeDispatch realtime,
            ICurrentActor actor)
        {
            _configs = configs;
            _actions = actions;
            _outbox = outbox;
            _realtime = realtime;
            _actor = actor;
        }

        public async Task<Result> Handle(Command command, CancellationToken cancellationToken)
        {
            var config = await _configs.GetAsync(command.GuideConfigId, cancellationToken)
                ?? throw new DomainException($"No config exists with id '{command.GuideConfigId}'.");

            if (config.Status == ConfigStatus.Closed)
                throw new DomainException("A closed config cannot be actioned.");

            var entry = GuideActionLogEntry.Record(
                config.Id,
                command.TestTaskId,
                command.PlatformId,
                config.TestPlanVersionId,
                command.Status,
                _actor.Current,
                DateTime.UtcNow,
                syncRequested: config.SyncEnabled);

            await _actions.AddAsync(entry, cancellationToken);

            if (config.SyncEnabled)
            {
                // Atomic with the action-log write (same transaction) — the sync can't be lost once the
                // action commits.
                _outbox.Enqueue(new MainDbActionRequested(
                    entry.Id,
                    command.TestTaskId,
                    command.PlatformId,
                    config.TestPlanVersionId,
                    command.Status.ToString(),
                    _actor.Current));
            }

            // Tell everyone working this config that a task was actioned — flushed only after the commit
            // (the dispatch behavior discards it if this request rolls back).
            _realtime.Publish(RealtimeGroups.Config(config.Id), new RealtimeEvent("TaskActioned", new
            {
                configId = config.Id,
                taskId = command.TestTaskId,
                platformId = command.PlatformId,
                status = command.Status.ToString(),
                byUser = _actor.Current,
            }));

            return new Result(entry.Id, entry.SyncState.ToString());
        }
    }
}
