using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using Onboarding.Application;
using Onboarding.Infrastructure.Persistence;

namespace Onboarding.Infrastructure.Behaviors;

/// <summary>
/// Onboarding-module unit of work: wraps each <see cref="IOnboardingCommand"/> in an OnboardingDbContext
/// transaction. The transaction logic lives in <see cref="TransactionBehaviorBase{TRequest,TResponse,TContext}"/>;
/// this just binds the module's DbContext and command marker.
/// </summary>
internal sealed class TransactionBehavior<TRequest, TResponse>
    : TransactionBehaviorBase<TRequest, TResponse, OnboardingDbContext>
    where TRequest : IRequest<TResponse>, IOnboardingCommand
{
    public TransactionBehavior(OnboardingDbContext db) : base(db)
    {
    }
}
