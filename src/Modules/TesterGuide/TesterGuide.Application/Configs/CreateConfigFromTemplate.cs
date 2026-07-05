using BuildingBlocks.Auditing;
using BuildingBlocks.Messaging;
using FluentValidation;
using TesterGuide.Application.Abstractions;
using TesterGuide.Domain;
using TestPlans.Contracts;

namespace TesterGuide.Application.Configs;

/// <summary>
/// Create a config from a template: the template supplies focus, mode, and sync defaults; the caller
/// supplies just the plan, version, and name. The plan/version are still validated against the primary
/// system's catalog.
/// </summary>
public static class CreateConfigFromTemplate
{
    public sealed record Command(Guid TemplateId, string Name, Guid TestPlanId, Guid TestPlanVersionId)
        : IRequest<Guid>, ITesterGuideCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.TemplateId).NotEmpty();
            RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
            RuleFor(command => command.TestPlanId).NotEmpty();
            RuleFor(command => command.TestPlanVersionId).NotEmpty();
        }
    }

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly IConfigTemplateRepository _templates;
        private readonly IGuideConfigRepository _configs;
        private readonly ITestPlanCatalog _catalog;
        private readonly ICurrentActor _actor;

        public Handler(
            IConfigTemplateRepository templates,
            IGuideConfigRepository configs,
            ITestPlanCatalog catalog,
            ICurrentActor actor)
        {
            _templates = templates;
            _configs = configs;
            _catalog = catalog;
            _actor = actor;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            var template = await _templates.GetAsync(command.TemplateId, cancellationToken)
                ?? throw new DomainException($"No template exists with id '{command.TemplateId}'.");

            if (!await _catalog.VersionExistsAsync(command.TestPlanId, command.TestPlanVersionId, cancellationToken))
                throw new DomainException(
                    $"Version '{command.TestPlanVersionId}' does not exist for test plan '{command.TestPlanId}'.");

            var config = GuideConfig.Create(
                command.Name, command.TestPlanId, command.TestPlanVersionId,
                template.FocusId, template.Mode, template.SyncEnabled, _actor.Current);

            await _configs.AddAsync(config, cancellationToken);
            return config.Id;
        }
    }
}
