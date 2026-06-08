using BuildingBlocks.Messaging;
using Library.Application.Abstractions;
using Library.Domain;

namespace Library.Application.Loans;

/// <summary>
/// Return a borrowed copy. Closes the active loan (assessing an overdue fine if late) and frees the copy.
/// If someone is waiting in the book's hold queue, the freed copy is held for them (ready for pickup)
/// rather than shelved — all in one Library-DB transaction.
/// </summary>
public static class ReturnLoan
{
    public sealed record Command(Guid CopyId) : IRequest<Result>, ILibraryCommand, IAuditableRequest;

    public sealed record Result(Guid LoanId, bool WasOverdue, decimal OverdueFine, decimal TotalFine, bool HeldForReservation);

    public sealed class Handler : IRequestHandler<Command, Result>
    {
        private readonly ILoanRepository _loans;
        private readonly IBookCopyRepository _copies;
        private readonly IReservationAllocator _allocator;

        public Handler(ILoanRepository loans, IBookCopyRepository copies, IReservationAllocator allocator)
        {
            _loans = loans;
            _copies = copies;
            _allocator = allocator;
        }

        public async Task<Result> Handle(Command command, CancellationToken cancellationToken)
        {
            var loan = await _loans.GetActiveByCopyAsync(command.CopyId, cancellationToken)
                ?? throw new DomainException($"No active loan exists for copy '{command.CopyId}'.");

            var copy = await _copies.GetAsync(command.CopyId, cancellationToken)
                ?? throw new DomainException($"No copy exists with id '{command.CopyId}'.");

            var overdueFine = loan.Return(DateOnly.FromDateTime(DateTime.UtcNow), CirculationPolicy.FinePerDayOverdue);
            copy.MarkReturned();

            // If the book has a waiting hold, the freed copy is held for the next person instead of shelved.
            var heldForReservation = await _allocator.TryHoldForNextAsync(copy, cancellationToken);

            return new Result(loan.Id, overdueFine > 0m, overdueFine, loan.FineAmount, heldForReservation);
        }
    }
}
