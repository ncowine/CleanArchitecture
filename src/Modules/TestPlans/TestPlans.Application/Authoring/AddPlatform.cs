using BuildingBlocks.Messaging;
using FluentValidation;
using TestPlans.Application.Abstractions;
using TestPlans.Domain;

namespace TestPlans.Application.Authoring;

/// <summary>Add a platform (variation) to the shared reference list.</summary>
public static class AddPlatform
{
    public sealed record Command(string Name, string Code)
        : IRequest<Guid>, ITestPlansCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.Name).NotEmpty().MaximumLength(100);
            RuleFor(command => command.Code).NotEmpty().MaximumLength(30);
        }
    }

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly IPlatformRepository _platforms;

        public Handler(IPlatformRepository platforms)
        {
            _platforms = platforms;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            var platform = Platform.Create(command.Name, command.Code);
            await _platforms.AddAsync(platform, cancellationToken);
            return platform.Id;
        }
    }
}
