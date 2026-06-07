using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Library.Application.Abstractions;
using Library.Application.Outbox;
using Library.Domain;

namespace Library.Application.Loans;

/// <summary>
/// Assess a fine on a loan and, only when the student's cumulative fines first cross the hold limit,
/// enqueue a <see cref="StudentHoldRequested"/> in the outbox (atomically with the fine). No validator:
/// the positive-amount rule is enforced by the domain, and an unknown loan is a not-found.
/// </summary>
public static class AssessFine
{
    public sealed record Command(Guid LoanId, decimal Amount)
        : IRequest<Result>, ILibraryCommand, IAuditableRequest;

    public sealed record Result(decimal TotalFines, bool HoldRequested);

    public sealed class Handler : IRequestHandler<Command, Result>
    {
        // The cumulative fine total that triggers a hold; configuration in a real app.
        private const decimal HoldThreshold = 20m;

        private readonly ILoanRepository _loans;
        private readonly IOutbox _outbox;

        public Handler(ILoanRepository loans, IOutbox outbox)
        {
            _loans = loans;
            _outbox = outbox;
        }

        public async Task<Result> Handle(Command command, CancellationToken cancellationToken)
        {
            var loan = await _loans.GetAsync(command.LoanId, cancellationToken)
                ?? throw new DomainException($"No loan exists with id '{command.LoanId}'.");

            var priorTotal = await _loans.GetFineTotalAsync(loan.StudentId, cancellationToken);

            loan.AssessFine(command.Amount);
            var newTotal = priorTotal + command.Amount;

            // "Only when required": enqueue once, on the transition from under the limit to over it.
            var holdRequested = priorTotal < HoldThreshold && newTotal >= HoldThreshold;
            if (holdRequested)
            {
                _outbox.Enqueue(new StudentHoldRequested(
                    loan.StudentId,
                    $"Outstanding library fines of {newTotal:0.00} exceed the {HoldThreshold:0.00} limit."));
            }

            return new Result(newTotal, holdRequested);
        }
    }
}
