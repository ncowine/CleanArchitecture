using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using Students.Application;
using Students.Infrastructure.Persistence;

namespace Students.Infrastructure.Behaviors;

/// <summary>
/// Students-module unit of work: wraps each <see cref="IStudentsCommand"/> in a StudentsDbContext
/// transaction. The transaction logic lives in <see cref="TransactionBehaviorBase{TRequest,TResponse,TContext}"/>;
/// this just binds the module's DbContext and command marker.
/// </summary>
internal sealed class TransactionBehavior<TRequest, TResponse>
    : TransactionBehaviorBase<TRequest, TResponse, StudentsDbContext>
    where TRequest : IRequest<TResponse>, IStudentsCommand
{
    public TransactionBehavior(StudentsDbContext db) : base(db)
    {
    }
}
