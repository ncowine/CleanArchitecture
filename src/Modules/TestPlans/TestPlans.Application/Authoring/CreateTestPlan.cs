using BuildingBlocks.Messaging;
using FluentValidation;
using TestPlans.Application.Abstractions;
using TestPlans.Domain;

namespace TestPlans.Application.Authoring;

/// <summary>Create a test plan (the root of a content tree).</summary>
public static class CreateTestPlan
{
    public sealed record Command(string Name, string Code)
        : IRequest<Guid>, ITestPlansCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
            RuleFor(command => command.Code).NotEmpty().MaximumLength(50);
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
            var plan = TestPlan.Create(command.Name, command.Code);
            await _plans.AddAsync(plan, cancellationToken);
            return plan.Id;
        }
    }
}
