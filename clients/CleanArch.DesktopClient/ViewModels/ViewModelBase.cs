using Prism.Mvvm;

namespace CleanArch.DesktopClient.ViewModels;

/// <summary>Base for screen ViewModels: shared busy/error state and a guarded async runner.</summary>
public abstract class ViewModelBase : BindableBase
{
    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    private string? _error;
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>Runs an async action with busy tracking and turns exceptions into <see cref="Error"/>.</summary>
    protected async Task RunAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Error = null;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            Error = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
