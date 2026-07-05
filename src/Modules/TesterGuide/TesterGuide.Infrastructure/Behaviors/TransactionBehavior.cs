using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using TesterGuide.Application;
using TesterGuide.Infrastructure.Persistence;

namespace TesterGuide.Infrastructure.Behaviors;

/// <summary>
/// Tester Guide-module unit of work: wraps each <see cref="ITesterGuideCommand"/> in a TesterGuideDbContext
/// transaction. The transaction logic lives in <see cref="TransactionBehaviorBase{TRequest,TResponse,TContext}"/>;
/// this just binds the module's DbContext and command marker.
/// </summary>
internal sealed class TransactionBehavior<TRequest, TResponse>
    : TransactionBehaviorBase<TRequest, TResponse, TesterGuideDbContext>
    where TRequest : IRequest<TResponse>, ITesterGuideCommand
{
    public TransactionBehavior(TesterGuideDbContext db) : base(db)
    {
    }
}
