using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using Equipment.Application;
using Equipment.Infrastructure.Persistence;

namespace Equipment.Infrastructure.Behaviors;

/// <summary>
/// Equipment-module unit of work: wraps each <see cref="IEquipmentCommand"/> in an EquipmentDbContext
/// transaction. The transaction logic lives in <see cref="TransactionBehaviorBase{TRequest,TResponse,TContext}"/>;
/// this just binds the module's DbContext and command marker.
/// </summary>
internal sealed class TransactionBehavior<TRequest, TResponse>
    : TransactionBehaviorBase<TRequest, TResponse, EquipmentDbContext>
    where TRequest : IRequest<TResponse>, IEquipmentCommand
{
    public TransactionBehavior(EquipmentDbContext db) : base(db)
    {
    }
}
