using Library.Application.Loans;
using Library.Application.Outbox;
using Library.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class AssessFineHandlerTests
{
    private static readonly DateOnly Today = new(2026, 1, 1);
    private static readonly DateOnly Due = new(2026, 3, 1);

    private static (AssessFine.Handler handler, FakeOutbox outbox, Loan loan) Build(decimal priorTotal)
    {
        var loan = Loan.Borrow(Guid.NewGuid(), Guid.NewGuid(), Today, Due);
        var loans = new FakeLoanRepository { FineTotal = priorTotal };
        loans.Seed(loan);
        var outbox = new FakeOutbox();
        return (new AssessFine.Handler(loans, outbox), outbox, loan);
    }

    [Fact]
    public async Task Fine_below_threshold_does_not_enqueue_a_hold()
    {
        var (handler, outbox, loan) = Build(priorTotal: 0m);

        var result = await handler.Handle(new AssessFine.Command(loan.Id, 5m), default);

        Assert.False(result.HoldRequested);
        Assert.Equal(5m, result.TotalFines);
        Assert.Empty(outbox.Events.OfType<StudentHoldRequested>()); // no hold below the limit
        Assert.Single(outbox.Events.OfType<LibraryFineAssessed>());  // but the fine is charged to the account
    }

    [Fact]
    public async Task Fine_crossing_threshold_enqueues_a_hold_for_the_student()
    {
        var (handler, outbox, loan) = Build(priorTotal: 0m);

        var result = await handler.Handle(new AssessFine.Command(loan.Id, 25m), default);

        Assert.True(result.HoldRequested);
        var hold = Assert.Single(outbox.Events.OfType<StudentHoldRequested>());
        Assert.Equal(loan.StudentId, hold.StudentId);
        Assert.Single(outbox.Events.OfType<LibraryFineAssessed>()); // the fine is also charged to the account
    }

    [Fact]
    public async Task Fine_when_already_over_threshold_does_not_enqueue_again()
    {
        var (handler, outbox, loan) = Build(priorTotal: 25m);

        var result = await handler.Handle(new AssessFine.Command(loan.Id, 10m), default);

        Assert.False(result.HoldRequested);
        Assert.Empty(outbox.Events.OfType<StudentHoldRequested>()); // already over — no second hold
        Assert.Single(outbox.Events.OfType<LibraryFineAssessed>());  // still charged
    }

    [Fact]
    public async Task Unknown_loan_throws()
    {
        var (handler, _, _) = Build(priorTotal: 0m);

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(new AssessFine.Command(Guid.NewGuid(), 5m), default));
    }
}
