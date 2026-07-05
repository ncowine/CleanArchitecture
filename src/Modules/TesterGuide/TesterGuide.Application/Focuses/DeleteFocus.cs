using BuildingBlocks.Messaging;
using FluentValidation;
using TesterGuide.Application.Abstractions;
using TesterGuide.Domain;

namespace TesterGuide.Application.Focuses;

/// <summary>
/// Delete a focus. (A guard against deleting a focus still referenced by configs/templates can be added
/// once those relationships matter; for now the focus manager owns its own list.)
/// </summary>
public static class DeleteFocus
{
    public sealed record Command(Guid FocusId) : IRequest<Guid>, ITesterGuideCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.FocusId).NotEmpty();
        }
    }

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly IFocusRepository _focuses;

        public Handler(IFocusRepository focuses)
        {
            _focuses = focuses;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            var focus = await _focuses.GetAsync(command.FocusId, cancellationToken)
                ?? throw new DomainException($"No focus exists with id '{command.FocusId}'.");

            _focuses.Remove(focus);
            return command.FocusId;
        }
    }
}
