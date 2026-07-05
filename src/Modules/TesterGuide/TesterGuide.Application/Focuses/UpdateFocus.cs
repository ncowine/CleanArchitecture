using BuildingBlocks.Messaging;
using FluentValidation;
using TesterGuide.Application.Abstractions;
using TesterGuide.Domain;

namespace TesterGuide.Application.Focuses;

/// <summary>Rename a focus / change its description.</summary>
public static class UpdateFocus
{
    public sealed record Command(Guid FocusId, string Name, string? Description)
        : IRequest<Guid>, ITesterGuideCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.FocusId).NotEmpty();
            RuleFor(command => command.Name).NotEmpty().MaximumLength(100);
            RuleFor(command => command.Description).MaximumLength(500);
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

            focus.Rename(command.Name, command.Description);
            return focus.Id;
        }
    }
}
