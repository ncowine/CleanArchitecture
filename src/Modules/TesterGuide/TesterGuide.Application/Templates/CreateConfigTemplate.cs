using BuildingBlocks.Messaging;
using FluentValidation;
using TesterGuide.Application.Abstractions;
using TesterGuide.Domain;

namespace TesterGuide.Application.Templates;

/// <summary>Create a config template (reusable defaults that speed up config creation).</summary>
public static class CreateConfigTemplate
{
    public sealed record Command(string Name, string? Description, Guid FocusId, GuideMode Mode, bool SyncEnabled)
        : IRequest<Guid>, ITesterGuideCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.Name).NotEmpty().MaximumLength(100);
            RuleFor(command => command.Description).MaximumLength(500);
            RuleFor(command => command.FocusId).NotEmpty();
            RuleFor(command => command.Mode).IsInEnum();
        }
    }

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly IConfigTemplateRepository _templates;
        private readonly IFocusRepository _focuses;

        public Handler(IConfigTemplateRepository templates, IFocusRepository focuses)
        {
            _templates = templates;
            _focuses = focuses;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            if (!await _focuses.ExistsAsync(command.FocusId, cancellationToken))
                throw new DomainException($"No focus exists with id '{command.FocusId}'.");

            var template = ConfigTemplate.Create(
                command.Name, command.Description, command.FocusId, command.Mode, command.SyncEnabled);
            await _templates.AddAsync(template, cancellationToken);
            return template.Id;
        }
    }
}
