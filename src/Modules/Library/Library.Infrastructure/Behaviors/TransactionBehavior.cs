using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using Library.Application;
using Library.Infrastructure.Persistence;

namespace Library.Infrastructure.Behaviors;

/// <summary>
/// Library-module unit of work: wraps each <see cref="ILibraryCommand"/> in a LibraryDbContext
/// transaction. The transaction logic lives in <see cref="TransactionBehaviorBase{TRequest,TResponse,TContext}"/>;
/// this just binds the module's DbContext and command marker.
/// </summary>
internal sealed class TransactionBehavior<TRequest, TResponse>
    : TransactionBehaviorBase<TRequest, TResponse, LibraryDbContext>
    where TRequest : IRequest<TResponse>, ILibraryCommand
{
    public TransactionBehavior(LibraryDbContext db) : base(db)
    {
    }
}
