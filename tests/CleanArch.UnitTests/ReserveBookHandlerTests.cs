using Library.Application.Reservations;
using Library.Domain;
using Students.Contracts;
using Xunit;

namespace CleanArch.UnitTests;

public class ReserveBookHandlerTests
{
    private static readonly DateOnly Acquired = new(2026, 1, 1);

    private static ReserveBook.Handler HandlerFor(
        FakeReservationRepository reservations,
        FakeBookRepository books,
        FakeBookCopyRepository copies,
        StudentSummary? student) =>
        new(reservations, books, copies, new FakeStudentDirectory(student));

    private static Book SeededBook(FakeBookRepository books)
    {
        var book = Book.Create("ISBN1", "Title", "Author", BookCategory.Science, 2000, null);
        books.Seed(book);
        return book;
    }

    private static BookCopy OnLoanCopy(Guid bookId)
    {
        var copy = BookCopy.Create(bookId, "BC-1", CopyCondition.Good, Acquired);
        copy.MarkOnLoan();
        return copy;
    }

    [Fact]
    public async Task Reserves_when_no_copy_is_available()
    {
        var books = new FakeBookRepository();
        var book = SeededBook(books);
        var copies = new FakeBookCopyRepository();
        copies.Seed(OnLoanCopy(book.Id)); // nothing available
        var handler = HandlerFor(
            new FakeReservationRepository(), books, copies, new StudentSummary(Guid.NewGuid(), "Ada", "a@b.com", "Active"));

        var result = await handler.Handle(new ReserveBook.Command(Guid.NewGuid(), book.Id), default);

        Assert.Equal(1, result.QueuePosition);
    }

    [Fact]
    public async Task Rejects_when_a_copy_is_available()
    {
        var books = new FakeBookRepository();
        var book = SeededBook(books);
        var copies = new FakeBookCopyRepository();
        copies.Seed(BookCopy.Create(book.Id, "BC-1", CopyCondition.Good, Acquired)); // available
        var handler = HandlerFor(
            new FakeReservationRepository(), books, copies, new StudentSummary(Guid.NewGuid(), "Ada", "a@b.com", "Active"));

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(new ReserveBook.Command(Guid.NewGuid(), book.Id), default));
    }

    [Fact]
    public async Task Rejects_a_duplicate_reservation_for_the_same_student()
    {
        var books = new FakeBookRepository();
        var book = SeededBook(books);
        var copies = new FakeBookCopyRepository();
        copies.Seed(OnLoanCopy(book.Id));
        var studentId = Guid.NewGuid();
        var reservations = new FakeReservationRepository();
        reservations.Seed(Reservation.Place(book.Id, studentId, 1, Acquired));
        var handler = HandlerFor(reservations, books, copies, new StudentSummary(studentId, "Ada", "a@b.com", "Active"));

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(new ReserveBook.Command(studentId, book.Id), default));
    }

    [Fact]
    public async Task A_withdrawn_student_cannot_reserve()
    {
        var books = new FakeBookRepository();
        var book = SeededBook(books);
        var handler = HandlerFor(
            new FakeReservationRepository(), books, new FakeBookCopyRepository(),
            new StudentSummary(Guid.NewGuid(), "Ada", "a@b.com", "Withdrawn"));

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(new ReserveBook.Command(Guid.NewGuid(), book.Id), default));
    }
}
