using System.Collections.ObjectModel;
using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.Navigation;
using Prism.Commands;
using Prism.Regions;

namespace CleanArch.DesktopClient.ViewModels;

/// <summary>A student's billing account: balance + paged statement, with charge/payment/waiver actions.</summary>
public sealed class BillingViewModel : ViewModelBase, INavigationAware
{
    private readonly IBillingApiClient _billing;
    private readonly INavigationService _navigation;

    public BillingViewModel(IBillingApiClient billing, INavigationService navigation)
    {
        _billing = billing;
        _navigation = navigation;

        NextPageCommand = new DelegateCommand(async () => { Page++; await ReloadAsync(); }, () => Page < TotalPages)
            .ObservesProperty(() => Page).ObservesProperty(() => TotalPages);
        PreviousPageCommand = new DelegateCommand(async () => { Page--; await ReloadAsync(); }, () => Page > 1)
            .ObservesProperty(() => Page);

        // Charges and waivers require a description (the API enforces it); payments don't.
        ChargeCommand = new DelegateCommand(async () => await ChargeAsync(), CanPostDescribed)
            .ObservesProperty(() => Amount).ObservesProperty(() => Description);
        PaymentCommand = new DelegateCommand(async () => await PaymentAsync(), () => Amount > 0)
            .ObservesProperty(() => Amount);
        WaiverCommand = new DelegateCommand(async () => await WaiveAsync(), CanPostDescribed)
            .ObservesProperty(() => Amount).ObservesProperty(() => Description);

        BackCommand = new DelegateCommand(() => _navigation.NavigateTo(ViewNames.Students));
    }

    public ObservableCollection<AccountEntry> Entries { get; } = new();

    // The charge categories the API accepts (Students.Domain.ChargeCategory).
    public IReadOnlyList<ChargeCategory> Categories { get; } = Enum.GetValues<ChargeCategory>();

    private Guid _studentId;
    public Guid StudentId { get => _studentId; private set => SetProperty(ref _studentId, value); }

    private decimal _balance;
    public decimal Balance { get => _balance; private set => SetProperty(ref _balance, value); }

    private int _page = 1;
    public int Page { get => _page; set => SetProperty(ref _page, value); }

    private int _pageSize = 20;
    public int PageSize { get => _pageSize; set => SetProperty(ref _pageSize, value); }

    private int _totalPages;
    public int TotalPages { get => _totalPages; set => SetProperty(ref _totalPages, value); }

    private int _totalCount;
    public int TotalCount { get => _totalCount; set => SetProperty(ref _totalCount, value); }

    private decimal _amount;
    public decimal Amount { get => _amount; set => SetProperty(ref _amount, value); }

    private string _description = string.Empty;
    public string Description { get => _description; set => SetProperty(ref _description, value); }

    private ChargeCategory _category = ChargeCategory.Tuition;
    public ChargeCategory Category { get => _category; set => SetProperty(ref _category, value); }

    private string? _notice;
    public string? Notice { get => _notice; private set => SetProperty(ref _notice, value); }

    public DelegateCommand NextPageCommand { get; }
    public DelegateCommand PreviousPageCommand { get; }
    public DelegateCommand ChargeCommand { get; }
    public DelegateCommand PaymentCommand { get; }
    public DelegateCommand WaiverCommand { get; }
    public DelegateCommand BackCommand { get; }

    private bool CanPostDescribed() => Amount > 0 && !string.IsNullOrWhiteSpace(Description);

    public Task LoadAsync(Guid studentId)
    {
        StudentId = studentId;
        Page = 1;
        return ReloadAsync();
    }

    public Task ChargeAsync() => PostAsync(() => _billing.ChargeAsync(StudentId, Amount, Category, Description));

    public Task PaymentAsync() => PostAsync(() => _billing.RecordPaymentAsync(StudentId, Amount, Description));

    public Task WaiveAsync() => PostAsync(() => _billing.WaiveAsync(StudentId, Amount, Description));

    private Task PostAsync(Func<Task<decimal>> action) => RunAsync(async () =>
    {
        var balance = await action();
        Notice = $"Done — new balance {balance:C}.";
        Amount = 0m;
        Description = string.Empty;

        // The new entry is the newest, so jump to the first page to show it.
        Page = 1;
        await LoadCoreAsync();
    });

    public Task ReloadAsync() => RunAsync(LoadCoreAsync);

    private async Task LoadCoreAsync()
    {
        var account = await _billing.GetAccountAsync(StudentId, Page, PageSize);
        Balance = account.Balance;

        Entries.Clear();
        foreach (var entry in account.Entries.Items)
        {
            Entries.Add(entry);
        }

        TotalCount = account.Entries.TotalCount;
        TotalPages = account.Entries.TotalPages;
    }

    public void OnNavigatedTo(NavigationContext navigationContext)
    {
        if (navigationContext.Parameters.TryGetValue<Guid>(ViewNames.StudentIdParameter, out var studentId))
        {
            _ = LoadAsync(studentId);
        }
    }

    public bool IsNavigationTarget(NavigationContext navigationContext) => false;

    public void OnNavigatedFrom(NavigationContext navigationContext)
    {
    }
}
