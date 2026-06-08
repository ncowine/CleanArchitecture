using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Persistence;

/// <summary>
/// Generic unit-of-work pipeline behavior: runs the handler, then SaveChanges + commit once inside a
/// transaction on <typeparamref name="TContext"/>; any exception rolls it back. Handlers and
/// repositories therefore stage changes and never call SaveChanges themselves.
/// <para>
/// Each module derives a tiny closed behavior that binds its own <c>DbContext</c> and command marker —
/// the marker (added as a constraint on the derived type) scopes which requests are wrapped, and keeps
/// each request committing against its <i>own</i> database (a transaction can't span databases; that's
/// what the outbox is for). The transaction logic itself lives here, once.
/// </para>
/// </summary>
public abstract class TransactionBehaviorBase<TRequest, TResponse, TContext> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TContext : DbContext
{
    private readonly TContext _db;

    protected TransactionBehaviorBase(TContext db)
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

        // The execution strategy is required for transactions with retry-on-failure providers
        // (e.g. SQL Server/MySQL); for SQLite it is a no-op passthrough.
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
