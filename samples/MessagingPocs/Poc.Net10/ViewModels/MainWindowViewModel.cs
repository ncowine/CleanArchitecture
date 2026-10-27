using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Equipment.Messages;
using Messaging.Hosting;
using Messaging.RabbitMQ;
using Poc.Shared;

namespace Poc.Net10.ViewModels;

/// <summary>
/// The equipment list, kept current by the API's events — over RabbitMQ or SignalR, whichever the app was started with.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly List<IDisposable> subscriptions = [];
    private readonly SynchronizationContext ui;
    private readonly EquipmentApi api;
    private DispatcherTimer? connectionWatch;
    private bool wasConnected;

    [ObservableProperty]
    private string title = $"net10 — modern app, API events only (pid {Environment.ProcessId})";

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
    public void ListenOverRabbitMq(IMessageSubscriber subscriber, RabbitMQBus bus)
    {
        Title += " · RabbitMQ";

        // ✅ DO pass the UI's SynchronizationContext, so each handler runs on the UI thread.
        // ✅ DO keep and dispose the subscriptions: they're strong references, unlike Prism's.
        subscriptions.Add(subscriber.Subscribe<EquipmentCreated>(List.Apply, ui));
        subscriptions.Add(subscriber.Subscribe<EquipmentUpdated>(List.Apply, ui));
        subscriptions.Add(subscriber.Subscribe<EquipmentDeleted>(List.Apply, ui));

        bus.Log += (sender, message) => ui.Post(state => AddLog($"[bus] {message}"), null);

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
        foreach (IDisposable subscription in subscriptions)
        {
            subscription.Dispose();
        }

        subscriptions.Clear();
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

    private async Task CheckConnectionAsync(RabbitMQBus bus)
    {
        bool connected = bus.IsConsumerConnected;
        if (connected == wasConnected)
        {
            return;
        }

        wasConnected = connected;
        if (connected)
        {
            Connection = "RabbitMQ: connected, bound to the API's exchange (CleanArch) only";
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
