using BuildingBlocks.Messaging;
using Library.Application.Abstractions;
using Library.Domain;

namespace Library.Application.Reservations;

/// <summary>
/// Pick up a ready reservation: issue a loan for the held copy to the reserving student and close the
/// reservation. Subject to the same borrow limit as a normal checkout.
/// </summary>
public static class FulfillReservation
{
    public sealed record Command(Guid ReservationId) : IRequest<Result>, ILibraryCommand, IAuditableRequest;

    public sealed record Result(Guid LoanId, Guid CopyId);

    public sealed class Handler : IRequestHandler<Command, Result>
    {
        private readonly IReservationRepository _reservations;
        private readonly IBookCopyRepository _copies;
        private readonly ILoanRepository _loans;

        public Handler(IReservationRepository reservations, IBookCopyRepository copies, ILoanRepository loans)
        {
            _reservations = reservations;
            _copies = copies;
            _loans = loans;
        }

        public async Task<Result> Handle(Command command, CancellationToken cancellationToken)
        {
            var reservation = await _reservations.GetAsync(command.ReservationId, cancellationToken)
                ?? throw new DomainException($"No reservation exists with id '{command.ReservationId}'.");
            if (reservation.Status != ReservationStatus.ReadyForPickup)
                throw new DomainException("This reservation is not ready for pickup.");

            var activeLoans = await _loans.CountActiveByStudentAsync(reservation.StudentId, cancellationToken);
            if (activeLoans >= CirculationPolicy.BorrowerLoanLimit)
                throw new DomainException(
                    $"The student already holds the maximum of {CirculationPolicy.BorrowerLoanLimit} active loans.");

            var copy = await _copies.GetAsync(reservation.HeldCopyId!.Value, cancellationToken)
                ?? throw new DomainException("The held copy could not be found.");

            copy.LendReserved();

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var loan = Loan.Borrow(reservation.StudentId, copy.Id, today, today.AddDays(CirculationPolicy.LoanPeriodDays));
            await _loans.AddAsync(loan, cancellationToken);

            reservation.Fulfill();
            return new Result(loan.Id, copy.Id);
        }
    }
}
