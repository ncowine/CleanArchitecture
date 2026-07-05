using BuildingBlocks.Auditing;
using BuildingBlocks.Messaging;
using FluentValidation;
using TesterGuide.Application.Abstractions;
using TesterGuide.Domain;
using TestPlans.Contracts;

namespace TesterGuide.Application.Configs;

/// <summary>
/// Create a guide config dedicated to a test plan at a specific version. This is the module's headline
/// cross-database read: the test plan + version live in the primary system's database, so the handler
/// validates them through the published <see cref="ITestPlanCatalog"/> contract (never by touching that
/// module's DbContext) before writing the config to its own database.
/// </summary>
public static class CreateGuideConfig
{
    public sealed record Command(
        string Name, Guid TestPlanId, Guid TestPlanVersionId, Guid FocusId, GuideMode Mode, bool SyncEnabled)
        : IRequest<Guid>, ITesterGuideCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
            RuleFor(command => command.TestPlanId).NotEmpty();
            RuleFor(command => command.TestPlanVersionId).NotEmpty();
            RuleFor(command => command.FocusId).NotEmpty();
            RuleFor(command => command.Mode).IsInEnum();
        }
    }

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly IGuideConfigRepository _configs;
        private readonly IFocusRepository _focuses;
        private readonly ITestPlanCatalog _catalog;
        private readonly ICurrentActor _actor;

        public Handler(
            IGuideConfigRepository configs,
            IFocusRepository focuses,
            ITestPlanCatalog catalog,
            ICurrentActor actor)
        {
            _configs = configs;
            _focuses = focuses;
            _catalog = catalog;
            _actor = actor;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            if (!await _focuses.ExistsAsync(command.FocusId, cancellationToken))
                throw new DomainException($"No focus exists with id '{command.FocusId}'.");

            // Cross-module / cross-database validation via the published contract.
            if (!await _catalog.VersionExistsAsync(command.TestPlanId, command.TestPlanVersionId, cancellationToken))
                throw new DomainException(
                    $"Version '{command.TestPlanVersionId}' does not exist for test plan '{command.TestPlanId}'.");

            var config = GuideConfig.Create(
                command.Name, command.TestPlanId, command.TestPlanVersionId,
                command.FocusId, command.Mode, command.SyncEnabled, _actor.Current);

            await _configs.AddAsync(config, cancellationToken);
            return config.Id;
        }
    }
}
