using CleanArch.DesktopClient.Api;
using CleanArch.DesktopClient.Navigation;
using Prism.Commands;

namespace CleanArch.DesktopClient.ViewModels;

public sealed class LoginViewModel : ViewModelBase
{
    private readonly IAuthSession _session;
    private readonly INavigationService _navigation;

    public LoginViewModel(IAuthSession session, INavigationService navigation)
    {
        _session = session;
        _navigation = navigation;
        SignInCommand = new DelegateCommand(async () => await SignInAsync(), () => !string.IsNullOrWhiteSpace(Actor))
            .ObservesProperty(() => Actor);
    }

    private string _actor = "registrar@uni";
    public string Actor
    {
        get => _actor;
        set => SetProperty(ref _actor, value);
    }

    public DelegateCommand SignInCommand { get; }

    public Task SignInAsync() => RunAsync(async () =>
    {
        await _session.SignInAsync(Actor, Array.Empty<string>());
        _navigation.NavigateTo(ViewNames.Students);
    });
}
