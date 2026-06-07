using BuildingBlocks.Messaging;
using Library.Application;
using Library.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Library.Infrastructure.Behaviors;

/// <summary>
/// Runs a Library-module command as a single unit of work against the Library database: the handler
/// stages its changes, then this behavior calls SaveChanges and commits once inside a transaction.
/// Any exception rolls the whole thing back.
/// <para>
/// This is a deliberate mirror of the Students module's behavior — each database gets its own,
/// because a transaction can only ever span one database. There is no single behavior that commits
/// both: cross-database consistency (Stage 2) is handled by the outbox pattern, not a shared
/// transaction. Only requests marked <see cref="ILibraryCommand"/> are wrapped.
/// </para>
/// </summary>
internal sealed class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, ILibraryCommand
{
    private readonly LibraryDbContext _db;

    public TransactionBehavior(LibraryDbContext db)
    {
        _db = db;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Already inside a transaction (e.g. a nested dispatch) — let the outer one own the commit.
        if (_db.Database.CurrentTransaction is not null)
        {
            return await next();
        }

        var strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            var response = await next();

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return response;
        });
    }
}
