using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Common.RabbitMQ;
using Poc.Legacy.Events.Shapes;
using Poc.Shared;
using Prism.Events;

namespace Poc.Net10.ViewModels;

/// <summary>
/// The equipment list, kept current by the API's events — over RabbitMQ or SignalR, whichever the app was started with.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly SynchronizationContext ui;
    private readonly EquipmentApi api;
    private DispatcherTimer? connectionWatch;
    private ShapeEventsLog? shapes;
    private bool wasConnected;

    [ObservableProperty]
    private string title = $"net10 — modern app, API events + POC shapes (pid {Environment.ProcessId})";

    [ObservableProperty]
    private string connection = "Connecting…";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private EquipmentRow? selected;

    public MainWindowViewModel(EquipmentApi api)
    {
        this.api = api;
        List = new EquipmentList(api);
        List.Logged += AddLog;

        // ❌ DON'T create this view model off the UI thread: Current would be null, and events would update the UI
        //    from a background thread.
        ui = SynchronizationContext.Current ?? throw new InvalidOperationException("Create the view model on the UI thread.");
    }

    public EquipmentList List { get; }

    public ObservableCollection<string> Log { get; } = [];

    /// <summary>RabbitMQ: subscribe now; the connection watch loads once the bus is connected.</summary>
    public void ListenOverRabbitMq(IEventAggregator events, RabbitMQService bus, BusLog busLog)
    {
        Title += " · RabbitMQ";

        // From the API: classes named after the API's events (Poc.Legacy.Events/FromApi), with its Equipment.Messages
        // classes as payloads.
        // ✅ DO subscribe with METHOD GROUPS on a long-lived object, and ThreadOption.UIThread to touch bound collections.
        // ❌ DON'T subscribe with a lambda that captures a local: Prism keeps a WEAK reference.
        events.GetEvent<Equipment.EquipmentCreated>().Subscribe(List.Apply, ThreadOption.UIThread);
        events.GetEvent<Equipment.EquipmentUpdated>().Subscribe(List.Apply, ThreadOption.UIThread);
        events.GetEvent<Equipment.EquipmentDeleted>().Subscribe(List.Apply, ThreadOption.UIThread);

        // The ten POC shape events, sent by the legacy apps. Kept in a field: Prism holds them weakly.
        shapes = new ShapeEventsLog(events, AddLog);

        busLog.Written += message => ui.Post(state => AddLog($"[bus] {message}"), null);

        // ✅ DO (re)load whenever the connection comes (back) up: the queue goes with the connection.
        connectionWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        connectionWatch.Tick += async (sender, e) => await CheckConnectionAsync(bus);
        connectionWatch.Start();
    }

    /// <summary>SignalR: subscribe, connect (retrying until the API is up), join the group, then load.</summary>
    public async Task ListenOverSignalRAsync(EquipmentLiveUpdates live)
    {
        Title += " · SignalR";

        // SignalR raises these on thread-pool threads: post each to the UI thread.
        live.Created += e => ui.Post(state => List.Apply(e), null);
        live.Updated += e => ui.Post(state => List.Apply(e), null);
        live.Deleted += e => ui.Post(state => List.Apply(e), null);
        live.StatusChanged += status => ui.Post(state => Connection = $"SignalR: {status}", null);

        // Nothing is queued for a disconnected client, so reload after every reconnect.
        live.Reconnected += () => ui.Post(async state => await List.LoadAsync(), null);

        // ✅ DO connect and join FIRST, then load (see EquipmentList.LoadAsync for why).
        if (await live.ConnectAsync(CancellationToken.None))
        {
            await List.LoadAsync();
        }
    }

    public void Dispose()
    {
        connectionWatch?.Stop();
    }

    [RelayCommand]
    private Task AddLaptopAsync() =>
        RunAsync(() => api.CreateLaptopAsync($"net10 (pid {Environment.ProcessId})"), "asked the API to create a laptop; waiting for its event");

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private Task DeleteSelectedAsync() =>
        RunAsync(() => api.DeleteAsync(Selected!.Id), $"asked the API to delete {Selected?.AssetTag}; waiting for its event");

    private bool CanDeleteSelected() => Selected is not null;

    [RelayCommand]
    private Task ReloadAsync() => List.LoadAsync();

    private async Task CheckConnectionAsync(RabbitMQService bus)
    {
        bool connected = bus.IsConsumerConnected;
        if (connected == wasConnected)
        {
            return;
        }

        wasConnected = connected;
        if (connected)
        {
            Connection = "RabbitMQ: connected, bound to the API's exchange (CleanArch), plus the shape events from the legacy apps";
            await List.LoadAsync();
        }
        else
        {
            Connection = "RabbitMQ: not connected — retrying (is RabbitMQ up, and has the API started at least once?)";
        }
    }

    private async Task RunAsync(Func<Task> call, string description)
    {
        try
        {
            await call();

            // ❌ DON'T change List here as well. The API's event does it, for every window alike, this one included.
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
