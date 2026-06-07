using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Students.Application;
using Students.Infrastructure.Persistence;

namespace Students.Infrastructure.Behaviors;

/// <summary>
/// Runs a Students-module command as a single unit of work: the handler stages its changes, then
/// this behavior calls SaveChanges and commits once inside a transaction. Any exception rolls the
/// whole thing back. Handlers and repositories therefore no longer call SaveChanges themselves.
/// <para>
/// Only requests marked <see cref="IStudentsCommand"/> are wrapped — the generic constraint also
/// stops the DI container from applying this behavior to other modules' requests.
/// </para>
/// </summary>
internal sealed class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, IStudentsCommand
{
    private readonly StudentsDbContext _db;

    public TransactionBehavior(StudentsDbContext db)
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

        // The execution strategy is required for transactions to work with retry-on-failure
        // providers (e.g. MySQL); for SQLite it is a no-op passthrough.
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
