using BuildingBlocks.Messaging;
using FluentValidation;
using Onboarding.Application.Abstractions;
using Onboarding.Domain;

namespace Onboarding.Application.Requests;

/// <summary>Register a new hire's onboarding request. Starts <see cref="OnboardingStatus.Pending"/> — approve it to run the saga.</summary>
public static class CreateOnboardingRequest
{
    public sealed record Command(
        string EmployeeName,
        DateOnly StartDate,
        string RequiredEquipmentCategory,
        string RequiredLicenceType,
        string RequiredAccessLevel)
        : IRequest<Guid>, IOnboardingCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.EmployeeName).NotEmpty().MaximumLength(200);
            RuleFor(command => command.StartDate).GreaterThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.UtcNow))
                .WithMessage("Start date cannot be in the past.");
            RuleFor(command => command.RequiredEquipmentCategory).NotEmpty().MaximumLength(50);
            RuleFor(command => command.RequiredLicenceType).NotEmpty().MaximumLength(50);
            RuleFor(command => command.RequiredAccessLevel).NotEmpty().MaximumLength(50);
        }
    }

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly IOnboardingRequestRepository _requests;

        public Handler(IOnboardingRequestRepository requests)
        {
            _requests = requests;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            var request = OnboardingRequest.Create(
                command.EmployeeName, command.StartDate,
                command.RequiredEquipmentCategory, command.RequiredLicenceType, command.RequiredAccessLevel);

            await _requests.AddAsync(request, cancellationToken);
            return request.Id;
        }
    }
}
