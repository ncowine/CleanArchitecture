using BuildingBlocks.Messaging;
using FluentValidation;
using TesterGuide.Application.Abstractions;
using TesterGuide.Domain;

namespace TesterGuide.Application.Configs;

/// <summary>Assign a user (an authenticated principal) to a config.</summary>
public static class AssignUser
{
    public sealed record Command(Guid GuideConfigId, string UserId, string DisplayName, ConfigRole Role)
        : IRequest<Guid>, ITesterGuideCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.GuideConfigId).NotEmpty();
            RuleFor(command => command.UserId).NotEmpty().MaximumLength(256);
            RuleFor(command => command.DisplayName).MaximumLength(256);
            RuleFor(command => command.Role).IsInEnum();
        }
    }

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly IGuideConfigRepository _configs;

        public Handler(IGuideConfigRepository configs)
        {
            _configs = configs;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            if (!await _configs.ExistsAsync(command.GuideConfigId, cancellationToken))
                throw new DomainException($"No config exists with id '{command.GuideConfigId}'.");

            if (await _configs.AssignmentExistsAsync(command.GuideConfigId, command.UserId, cancellationToken))
                throw new DomainException($"User '{command.UserId}' is already assigned to this config.");

            var assignment = ConfigAssignment.Create(
                command.GuideConfigId, command.UserId, command.DisplayName, command.Role, DateTime.UtcNow);

            await _configs.AddAssignmentAsync(assignment, cancellationToken);
            return assignment.Id;
        }
    }
}
