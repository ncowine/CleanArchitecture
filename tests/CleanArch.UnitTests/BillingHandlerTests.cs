using Students.Application.Billing;
using Students.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class BillingHandlerTests
{
    private static readonly DateOnly Dob = new(2000, 1, 1);
    private static readonly DateOnly Enrolled = new(2024, 9, 1);

    private static Student NewStudent() => Student.Create("Ada", "Lovelace", "a@b.com", Dob, Enrolled);

    [Fact]
    public async Task Charge_opens_the_account_on_first_use_and_sets_the_balance()
    {
        var student = NewStudent();
        var students = new FakeStudentRepository(student);
        var accounts = new FakeStudentAccountRepository();
        var handler = new ChargeAccount.Handler(new AccountCharger(accounts, students), students);

        var result = await handler.Handle(
            new ChargeAccount.Command(student.Id, 1500m, ChargeCategory.Tuition, "Fall tuition"), default);

        Assert.Equal(1500m, result.Balance);
        Assert.Single(accounts.Added);
    }

    [Fact]
    public async Task Charge_for_an_unknown_student_throws()
    {
        var students = new FakeStudentRepository(null);
        var handler = new ChargeAccount.Handler(new AccountCharger(new FakeStudentAccountRepository(), students), students);

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(new ChargeAccount.Command(Guid.NewGuid(), 100m, ChargeCategory.Fee, "Fee"), default));
    }

    [Fact]
    public async Task Payment_reduces_the_balance()
    {
        var studentId = Guid.NewGuid();
        var account = StudentAccount.Open(studentId);
        account.Charge(200m, ChargeCategory.Fee, "Fee", new DateOnly(2026, 1, 1));
        var accounts = new FakeStudentAccountRepository();
        accounts.Seed(account);
        var handler = new RecordPayment.Handler(accounts);

        var result = await handler.Handle(new RecordPayment.Command(studentId, 50m, "Card"), default);

        Assert.Equal(150m, result.Balance);
    }

    [Fact]
    public async Task Payment_without_an_account_throws()
    {
        var handler = new RecordPayment.Handler(new FakeStudentAccountRepository());

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(new RecordPayment.Command(Guid.NewGuid(), 50m, null), default));
    }

    [Fact]
    public async Task Waiver_reduces_the_balance()
    {
        var studentId = Guid.NewGuid();
        var account = StudentAccount.Open(studentId);
        account.Charge(30m, ChargeCategory.LibraryFine, "Overdue", new DateOnly(2026, 1, 1));
        var accounts = new FakeStudentAccountRepository();
        accounts.Seed(account);
        var handler = new WaiveCharge.Handler(accounts);

        var result = await handler.Handle(new WaiveCharge.Command(studentId, 30m, "Waived"), default);

        Assert.Equal(0m, result.Balance);
    }
}
