using Students.Application.Billing;
using Students.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class AccountChargerTests
{
    private static AccountCharger Charger(FakeStudentAccountRepository accounts, FakeStudentRepository students) =>
        new(accounts, students);

    [Fact]
    public async Task Charge_opens_the_account_and_returns_the_balance()
    {
        var accounts = new FakeStudentAccountRepository();
        var students = new FakeStudentRepository();
        var charger = Charger(accounts, students);

        var balance = await charger.ChargeAsync(Guid.NewGuid(), 250m, ChargeCategory.Fee, "Lab fee", default);

        Assert.Equal(250m, balance);
        Assert.Single(accounts.Added);
        Assert.Empty(students.AddedHolds);
    }

    [Fact]
    public async Task A_charge_crossing_the_threshold_places_a_financial_hold()
    {
        var studentId = Guid.NewGuid();
        var accounts = new FakeStudentAccountRepository();
        var students = new FakeStudentRepository();
        var charger = Charger(accounts, students);

        await charger.ChargeAsync(
            studentId, BillingPolicy.FinancialHoldThreshold, ChargeCategory.Tuition, "Tuition", default);

        var hold = Assert.Single(students.AddedHolds);
        Assert.Equal(studentId, hold.StudentId);
    }

    [Fact]
    public async Task A_charge_under_the_threshold_places_no_hold()
    {
        var accounts = new FakeStudentAccountRepository();
        var students = new FakeStudentRepository();
        var charger = Charger(accounts, students);

        await charger.ChargeAsync(
            Guid.NewGuid(), BillingPolicy.FinancialHoldThreshold - 1m, ChargeCategory.Fee, "Fee", default);

        Assert.Empty(students.AddedHolds);
    }

    [Fact]
    public async Task A_second_charge_already_over_the_threshold_does_not_place_another_hold()
    {
        var studentId = Guid.NewGuid();
        var accounts = new FakeStudentAccountRepository();
        var students = new FakeStudentRepository();
        var charger = Charger(accounts, students);

        await charger.ChargeAsync(studentId, BillingPolicy.FinancialHoldThreshold, ChargeCategory.Tuition, "first", default);
        await charger.ChargeAsync(studentId, 100m, ChargeCategory.Fee, "second", default);

        Assert.Single(students.AddedHolds); // only on the crossing
    }
}
