using BuildingBlocks.Messaging;
using FluentValidation;
using Library.Application.Abstractions;
using Library.Domain;
using Students.Contracts;

namespace Library.Application.Reservations;

/// <summary>
/// Place a hold on a book. Allowed only when no copy is currently available (otherwise just borrow one).
/// The student joins the end of the queue.
/// </summary>
public static class ReserveBook
{
    public sealed record Command(Guid StudentId, Guid BookId)
        : IRequest<Result>, ILibraryCommand, IAuditableRequest;

    public sealed record Result(Guid ReservationId, int QueuePosition);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.StudentId).NotEmpty();
            RuleFor(command => command.BookId).NotEmpty();
        }
    }

    public sealed class Handler : IRequestHandler<Command, Result>
    {
        private readonly IReservationRepository _reservations;
        private readonly IBookRepository _books;
        private readonly IBookCopyRepository _copies;
        private readonly IStudentDirectory _students;

        public Handler(
            IReservationRepository reservations,
            IBookRepository books,
            IBookCopyRepository copies,
            IStudentDirectory students)
        {
            _reservations = reservations;
            _books = books;
            _copies = copies;
            _students = students;
        }

        public async Task<Result> Handle(Command command, CancellationToken cancellationToken)
        {
            var student = await _students.GetAsync(command.StudentId, cancellationToken)
                ?? throw new DomainException($"No student exists with id '{command.StudentId}'.");
            if (student.Status is "Withdrawn" or "Graduated")
                throw new DomainException($"A {student.Status} student cannot reserve.");

            if (!await _books.ExistsAsync(command.BookId, cancellationToken))
                throw new DomainException($"No book exists with id '{command.BookId}'.");

            if (await _copies.CountAvailableAsync(command.BookId, cancellationToken) > 0)
                throw new DomainException("A copy is available — borrow it directly instead of reserving.");

            if (await _reservations.HasActiveForStudentAsync(command.StudentId, command.BookId, cancellationToken))
                throw new DomainException("The student already has an active reservation for this book.");

            var position = await _reservations.CountPendingAsync(command.BookId, cancellationToken) + 1;
            var reservation = Reservation.Place(
                command.BookId, command.StudentId, position, DateOnly.FromDateTime(DateTime.UtcNow));

            await _reservations.AddAsync(reservation, cancellationToken);
            return new Result(reservation.Id, position);
        }
    }
}
