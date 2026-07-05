using BuildingBlocks.Messaging;
using FluentValidation;
using TesterGuide.Application.Abstractions;
using TesterGuide.Domain;

namespace TesterGuide.Application.Content;

/// <summary>
/// Content manager: mark a primary-system task enabled (or disabled) for the tool. Upserts the overlay row
/// keyed by (test plan, task) — metadata layered on the system of record without modifying it.
/// </summary>
public static class SetTaskContentEnabled
{
    public sealed record Command(Guid TestPlanId, Guid TestTaskId, bool IsEnabled)
        : IRequest<Result>, ITesterGuideCommand, IAuditableRequest;

    public sealed record Result(Guid TestPlanId, Guid TestTaskId, bool IsEnabled);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.TestPlanId).NotEmpty();
            RuleFor(command => command.TestTaskId).NotEmpty();
        }
    }

    public sealed class Handler : IRequestHandler<Command, Result>
    {
        private readonly IContentSelectionRepository _content;

        public Handler(IContentSelectionRepository content)
        {
            _content = content;
        }

        public async Task<Result> Handle(Command command, CancellationToken cancellationToken)
        {
            var existing = await _content.GetAsync(command.TestPlanId, command.TestTaskId, cancellationToken);

            if (existing is null)
            {
                var selection = ContentSelection.Create(command.TestPlanId, command.TestTaskId, command.IsEnabled);
                await _content.AddAsync(selection, cancellationToken);
            }
            else
            {
                existing.SetEnabled(command.IsEnabled);
            }

            return new Result(command.TestPlanId, command.TestTaskId, command.IsEnabled);
        }
    }
}
