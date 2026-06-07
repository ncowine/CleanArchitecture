using Prism.Mvvm;

namespace CleanArch.DesktopClient.ViewModels;

public class MainWindowViewModel : BindableBase
{
    private string _title = "CleanArch Desktop Client";
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }
}
