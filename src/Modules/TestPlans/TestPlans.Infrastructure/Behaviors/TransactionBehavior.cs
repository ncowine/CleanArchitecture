using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using TestPlans.Application;
using TestPlans.Infrastructure.Persistence;

namespace TestPlans.Infrastructure.Behaviors;

/// <summary>
/// Test Plans-module unit of work: wraps each <see cref="ITestPlansCommand"/> in a TestPlansDbContext
/// transaction. The transaction logic lives in <see cref="TransactionBehaviorBase{TRequest,TResponse,TContext}"/>;
/// this just binds the module's DbContext and command marker.
/// </summary>
internal sealed class TransactionBehavior<TRequest, TResponse>
    : TransactionBehaviorBase<TRequest, TResponse, TestPlansDbContext>
    where TRequest : IRequest<TResponse>, ITestPlansCommand
{
    public TransactionBehavior(TestPlansDbContext db) : base(db)
    {
    }
}
