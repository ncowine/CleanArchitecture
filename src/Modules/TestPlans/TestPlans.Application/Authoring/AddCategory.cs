using BuildingBlocks.Messaging;
using FluentValidation;
using TestPlans.Application.Abstractions;
using TestPlans.Domain;

namespace TestPlans.Application.Authoring;

/// <summary>Add a category to a test plan.</summary>
public static class AddCategory
{
    public sealed record Command(Guid TestPlanId, string Name, int Order)
        : IRequest<Guid>, ITestPlansCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.TestPlanId).NotEmpty();
            RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
            RuleFor(command => command.Order).GreaterThanOrEqualTo(0);
        }
    }

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly ITestPlanRepository _plans;
        private readonly ITestContentRepository _content;

        public Handler(ITestPlanRepository plans, ITestContentRepository content)
        {
            _plans = plans;
            _content = content;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            if (!await _plans.ExistsAsync(command.TestPlanId, cancellationToken))
                throw new DomainException($"No test plan exists with id '{command.TestPlanId}'.");

            var category = Category.Create(command.TestPlanId, command.Name, command.Order);
            await _content.AddCategoryAsync(category, cancellationToken);
            return category.Id;
        }
    }
}
