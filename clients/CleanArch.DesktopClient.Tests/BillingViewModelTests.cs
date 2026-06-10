using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.Navigation;
using CleanArch.DesktopClient.ViewModels;
using Xunit;

namespace CleanArch.DesktopClient.Tests;

public class BillingViewModelTests
{
    private static AccountEntry Entry(string kind = "Charge") =>
        new(Guid.NewGuid(), kind, "Tuition", 100m, "Fall tuition", new DateOnly(2026, 1, 15));

    private static StudentAccount Account(decimal balance, params AccountEntry[] entries) =>
        new(Guid.NewGuid(), balance, new PagedResult<AccountEntry>(entries, 1, 20, entries.Length, entries.Length == 0 ? 0 : 1));

    [Fact]
    public async Task Load_populates_balance_and_entries()
    {
        var api = new FakeBillingApiClient { Account = Account(250m, Entry(), Entry("Payment")) };
        var vm = new BillingViewModel(api, new FakeNavigationService());

        await vm.LoadAsync(Guid.NewGuid());

        Assert.Equal(250m, vm.Balance);
        Assert.Equal(2, vm.Entries.Count);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Charge_posts_amount_category_description_and_reloads()
    {
        var studentId = Guid.NewGuid();
        var api = new FakeBillingApiClient { Account = Account(0m), NextBalance = 100m };
        var vm = new BillingViewModel(api, new FakeNavigationService());
        await vm.LoadAsync(studentId);

        vm.Amount = 100m;
        vm.Description = "Lab fee";
        vm.Category = ChargeCategory.Fee;
        await vm.ChargeAsync();

        var charge = Assert.Single(api.Charges);
        Assert.Equal(studentId, charge.studentId);
        Assert.Equal(100m, charge.amount);
        Assert.Equal(ChargeCategory.Fee, charge.category);
        Assert.Equal("Lab fee", charge.description);
        Assert.Equal(2, api.GetAccountCallCount); // initial load + reload after charge
        Assert.Equal(0m, vm.Amount);              // inputs cleared
        Assert.Equal(string.Empty, vm.Description);
        Assert.Contains("new balance", vm.Notice);
    }

    [Fact]
    public async Task Payment_posts_to_the_payments_endpoint()
    {
        var studentId = Guid.NewGuid();
        var api = new FakeBillingApiClient { Account = Account(100m), NextBalance = 40m };
        var vm = new BillingViewModel(api, new FakeNavigationService());
        await vm.LoadAsync(studentId);

        vm.Amount = 60m;
        await vm.PaymentAsync();

        var payment = Assert.Single(api.Payments);
        Assert.Equal(studentId, payment.studentId);
        Assert.Equal(60m, payment.amount);
        Assert.Empty(api.Charges);
    }

    [Fact]
    public async Task Waive_posts_to_the_waivers_endpoint()
    {
        var studentId = Guid.NewGuid();
        var api = new FakeBillingApiClient { Account = Account(100m), NextBalance = 90m };
        var vm = new BillingViewModel(api, new FakeNavigationService());
        await vm.LoadAsync(studentId);

        vm.Amount = 10m;
        vm.Description = "Goodwill";
        await vm.WaiveAsync();

        var waiver = Assert.Single(api.Waivers);
        Assert.Equal(studentId, waiver.studentId);
        Assert.Equal(10m, waiver.amount);
        Assert.Equal("Goodwill", waiver.description);
    }

    [Fact]
    public async Task Charge_and_waive_require_an_amount_and_description_payment_only_an_amount()
    {
        var api = new FakeBillingApiClient { Account = Account(0m) };
        var vm = new BillingViewModel(api, new FakeNavigationService());
        await vm.LoadAsync(Guid.NewGuid());

        vm.Amount = 50m; // amount only, no description

        Assert.True(vm.PaymentCommand.CanExecute());
        Assert.False(vm.ChargeCommand.CanExecute());
        Assert.False(vm.WaiverCommand.CanExecute());

        vm.Description = "Tuition";

        Assert.True(vm.ChargeCommand.CanExecute());
        Assert.True(vm.WaiverCommand.CanExecute());
    }

    [Fact]
    public void Posting_is_disabled_with_a_zero_or_negative_amount()
    {
        var vm = new BillingViewModel(new FakeBillingApiClient(), new FakeNavigationService())
        {
            Amount = 0m,
            Description = "x",
        };

        Assert.False(vm.ChargeCommand.CanExecute());
        Assert.False(vm.PaymentCommand.CanExecute());
        Assert.False(vm.WaiverCommand.CanExecute());
    }

    [Fact]
    public async Task Back_navigates_to_students()
    {
        var navigation = new FakeNavigationService();
        var vm = new BillingViewModel(new FakeBillingApiClient { Account = Account(0m) }, navigation);
        await vm.LoadAsync(Guid.NewGuid());

        vm.BackCommand.Execute();

        var (view, _) = Assert.Single(navigation.Navigations);
        Assert.Equal(ViewNames.Students, view);
    }

    [Fact]
    public async Task Charge_error_is_surfaced_and_busy_cleared()
    {
        var api = new ThrowingBillingApiClient(new ApiException("No student exists.", 400));
        var vm = new BillingViewModel(api, new FakeNavigationService());

        vm.Amount = 10m;
        vm.Description = "x";
        await vm.ChargeAsync();

        Assert.Equal("No student exists.", vm.Error);
        Assert.False(vm.IsBusy);
    }

    private sealed class ThrowingBillingApiClient : IBillingApiClient
    {
        private readonly Exception _error;
        public ThrowingBillingApiClient(Exception error) => _error = error;

        public Task<StudentAccount> GetAccountAsync(Guid studentId, int page = 1, int pageSize = 20, CancellationToken ct = default) =>
            Task.FromResult(new StudentAccount(studentId, 0m, new PagedResult<AccountEntry>(new List<AccountEntry>(), 1, 20, 0, 0)));

        public Task<decimal> ChargeAsync(Guid studentId, decimal amount, ChargeCategory category, string description, CancellationToken ct = default) =>
            Task.FromException<decimal>(_error);

        public Task<decimal> RecordPaymentAsync(Guid studentId, decimal amount, string? description, CancellationToken ct = default) =>
            Task.FromException<decimal>(_error);

        public Task<decimal> WaiveAsync(Guid studentId, decimal amount, string description, CancellationToken ct = default) =>
            Task.FromException<decimal>(_error);
    }
}
