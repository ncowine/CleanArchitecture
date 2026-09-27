using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poc.Shared;

namespace Poc3.SignalR.ViewModels;

/// <summary>
/// The equipment list, kept current by SignalR. Loads over HTTP, listens over SignalR, changes things over HTTP.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly EquipmentApi _api;
    private readonly EquipmentLiveUpdates _live;
    private readonly SynchronizationContext _ui;

    [ObservableProperty]
    private string _connection = "Starting…";

    [ObservableProperty]
    private int _watchers;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private EquipmentRow? _selected;

    public MainWindowViewModel(EquipmentApi api, EquipmentLiveUpdates live)
    {
        _api = api;
        _live = live;
        List = new EquipmentList(api);
        List.Logged += AddLog;
        _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("Create the view model on the UI thread.");

        // SignalR raises these on thread-pool threads.
        // ❌ DON'T update the list straight from them: WPF throws when a bound collection changes off the UI thread.
        // ✅ DO post every one to the UI thread. That also serialises them: they're applied one at a time, in order.
        _live.Created += e => _ui.Post(_ => List.Apply(e), null);
        _live.Updated += e => _ui.Post(_ => List.Apply(e), null);
        _live.Deleted += e => _ui.Post(_ => List.Apply(e), null);
        _live.PresenceChanged += count => _ui.Post(_ => Watchers = count, null);
        _live.StatusChanged += status => _ui.Post(_ => Connection = status, null);

        // After a reconnect the group has been re-joined; everything sent meanwhile is gone, so reload.
        _live.Reconnected += () => _ui.Post(async _ => await List.LoadAsync(), null);
    }

    public string Title { get; } = $"POC 3 — SignalR from the API (pid {Environment.ProcessId})";

    public EquipmentList List { get; }

    public ObservableCollection<string> Log { get; } = [];

    /// <summary>Called once the window is showing.</summary>
    public async Task StartAsync()
    {
        // ✅ DO connect and join FIRST, then load (see EquipmentList.LoadAsync for why).
        await _live.ConnectAsync(CancellationToken.None);
        await List.LoadAsync();
    }

    [RelayCommand]
    private Task AddLaptopAsync() =>
        RunAsync(() => _api.CreateLaptopAsync("POC 3"), "asked the API to create a laptop; waiting for the SignalR event");

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private Task DeleteSelectedAsync() =>
        RunAsync(() => _api.DeleteAsync(Selected!.Id), $"asked the API to delete {Selected?.AssetTag}; waiting for the event");

    private bool CanDeleteSelected() => Selected is not null;

    [RelayCommand]
    private Task ReloadAsync() => List.LoadAsync();

    private async Task RunAsync(Func<Task> call, string description)
    {
        try
        {
            await call();

            // ❌ DON'T change the list here as well. The API pushes the event to everyone in the group, this window
            //    included, and that's what updates it. Doing both shows the row twice, or makes you write "ignore my
            //    own echo" logic that goes wrong the first time two windows are open.
            AddLog(description);
        }
        catch (HttpRequestException ex)
        {
            AddLog($"request failed: {ex.Message}");
        }
    }

    private void AddLog(string line)
    {
        Log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {line}");
    }
}
