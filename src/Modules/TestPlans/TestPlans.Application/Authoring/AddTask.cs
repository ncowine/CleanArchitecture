using BuildingBlocks.Messaging;
using FluentValidation;
using TestPlans.Application.Abstractions;
using TestPlans.Domain;

namespace TestPlans.Application.Authoring;

/// <summary>Add a task to a sub-category.</summary>
public static class AddTask
{
    public sealed record Command(Guid SubCategoryId, string Name, string? Description, TaskMode Mode)
        : IRequest<Guid>, ITestPlansCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.SubCategoryId).NotEmpty();
            RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
            RuleFor(command => command.Mode).IsInEnum();
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
            if (!await _content.SubCategoryExistsAsync(command.SubCategoryId, cancellationToken))
                throw new DomainException($"No sub-category exists with id '{command.SubCategoryId}'.");

            var task = TestTask.Create(command.SubCategoryId, command.Name, command.Description, command.Mode);
            await _content.AddTaskAsync(task, cancellationToken);
            return task.Id;
        }
    }
}
