using Students.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class StudentAccountTests
{
    private static readonly DateOnly Today = new(2026, 1, 1);

    private static StudentAccount Account() => StudentAccount.Open(Guid.NewGuid());

    [Fact]
    public void Open_starts_with_a_zero_balance()
    {
        var account = Account();

        Assert.Equal(0m, account.Balance);
        Assert.Empty(account.Entries);
    }

    [Fact]
    public void Charge_increases_the_balance_and_records_the_category()
    {
        var account = Account();

        account.Charge(1500m, ChargeCategory.Tuition, "Fall tuition", Today);

        Assert.Equal(1500m, account.Balance);
        var entry = Assert.Single(account.Entries);
        Assert.Equal(AccountEntryKind.Charge, entry.Kind);
        Assert.Equal(ChargeCategory.Tuition, entry.Category);
    }

    [Fact]
    public void Payment_decreases_the_balance_and_has_no_category()
    {
        var account = Account();
        account.Charge(100m, ChargeCategory.Fee, "Lab fee", Today);

        account.RecordPayment(40m, "Card payment", Today);

        Assert.Equal(60m, account.Balance);
        Assert.Null(account.Entries.Last().Category);
    }

    [Fact]
    public void Waiver_decreases_the_balance()
    {
        var account = Account();
        account.Charge(25m, ChargeCategory.LibraryFine, "Overdue fine", Today);

        account.Waive(25m, "Goodwill waiver", Today);

        Assert.Equal(0m, account.Balance);
    }

    [Fact]
    public void Overpayment_leaves_a_credit_balance()
    {
        var account = Account();
        account.Charge(50m, ChargeCategory.Fee, "Fee", Today);

        account.RecordPayment(70m, "Payment", Today);

        Assert.Equal(-20m, account.Balance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_non_positive_amount_throws(decimal amount) =>
        Assert.Throws<DomainException>(() => Account().Charge(amount, ChargeCategory.Fee, "x", Today));

    [Fact]
    public void An_empty_description_throws() =>
        Assert.Throws<DomainException>(() => Account().Charge(10m, ChargeCategory.Fee, "  ", Today));

    [Fact]
    public void A_sourced_charge_is_tracked_for_idempotency()
    {
        var account = Account();
        var source = Guid.NewGuid();

        account.Charge(20m, ChargeCategory.LibraryFine, "Library fine", Today, source);

        Assert.True(account.HasEntryFrom(source));
        Assert.False(account.HasEntryFrom(Guid.NewGuid()));
    }
}
