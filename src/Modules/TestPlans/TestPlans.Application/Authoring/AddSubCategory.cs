using BuildingBlocks.Messaging;
using FluentValidation;
using TestPlans.Application.Abstractions;
using TestPlans.Domain;

namespace TestPlans.Application.Authoring;

/// <summary>Add a sub-category to a category.</summary>
public static class AddSubCategory
{
    public sealed record Command(Guid CategoryId, string Name, int Order)
        : IRequest<Guid>, ITestPlansCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.CategoryId).NotEmpty();
            RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
            RuleFor(command => command.Order).GreaterThanOrEqualTo(0);
        }
    }

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly ITestContentRepository _content;

        public Handler(ITestContentRepository content)
        {
            _content = content;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            if (!await _content.CategoryExistsAsync(command.CategoryId, cancellationToken))
                throw new DomainException($"No category exists with id '{command.CategoryId}'.");

            var subCategory = SubCategory.Create(command.CategoryId, command.Name, command.Order);
            await _content.AddSubCategoryAsync(subCategory, cancellationToken);
            return subCategory.Id;
        }
    }
}
