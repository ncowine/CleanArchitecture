using BuildingBlocks.Messaging;
using FluentValidation;
using TesterGuide.Application.Abstractions;
using TesterGuide.Domain;

namespace TesterGuide.Application.Focuses;

/// <summary>Create a focus (a named label attached to configs).</summary>
public static class CreateFocus
{
    public sealed record Command(string Name, string? Description)
        : IRequest<Guid>, ITesterGuideCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
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
            var focus = Focus.Create(command.Name, command.Description);
            await _focuses.AddAsync(focus, cancellationToken);
            return focus.Id;
        }
    }
}
