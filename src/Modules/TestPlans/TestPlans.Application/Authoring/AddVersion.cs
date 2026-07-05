using BuildingBlocks.Messaging;
using FluentValidation;
using TestPlans.Application.Abstractions;
using TestPlans.Domain;

namespace TestPlans.Application.Authoring;

/// <summary>Add a (Version.SubVersion) to a test plan.</summary>
public static class AddVersion
{
    public sealed record Command(Guid TestPlanId, int Version, int SubVersion)
        : IRequest<Guid>, ITestPlansCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.TestPlanId).NotEmpty();
            RuleFor(command => command.Version).GreaterThanOrEqualTo(0);
            RuleFor(command => command.SubVersion).GreaterThanOrEqualTo(0);
        }
    }

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly ITestPlanRepository _plans;

        public Handler(ITestPlanRepository plans)
        {
            _plans = plans;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            if (!await _plans.ExistsAsync(command.TestPlanId, cancellationToken))
                throw new DomainException($"No test plan exists with id '{command.TestPlanId}'.");

            var version = TestPlanVersion.Create(command.TestPlanId, command.Version, command.SubVersion);
            await _plans.AddVersionAsync(version, cancellationToken);
            return version.Id;
        }
    }
}
