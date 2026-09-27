using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Equipment.Messages;
using Messaging.Hosting;
using Messaging.Prism;
using Messaging.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using Poc.Shared;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;

namespace Poc1.PrismRabbitMq.ViewModels;

/// <summary>
/// The equipment list, kept current by the API's RabbitMQ events, received as Prism events.
/// </summary>
public sealed class MainWindowViewModel : BindableBase
{
    private readonly EquipmentApi _api;
    private readonly RabbitMQBus _bus;
    private readonly DispatcherTimer _connectionWatch;
    private bool _wasConnected;
    private string _connection = "Connecting…";
    private EquipmentRow? _selected;

    public MainWindowViewModel(IEventAggregator eventAggregator, MessagingClient client, EquipmentApi api)
    {
        _api = api;
        List = new EquipmentList(api);
        List.Logged += AddLog;

        // ✅ DO subscribe with METHOD GROUPS on a long-lived object (List lives as long as this view model), and
        //    ThreadOption.UIThread so the handler may touch bound collections directly.
        eventAggregator.GetEvent<MessageEvent<EquipmentCreated>>().Subscribe(List.Apply, ThreadOption.UIThread);
        eventAggregator.GetEvent<MessageEvent<EquipmentUpdated>>().Subscribe(List.Apply, ThreadOption.UIThread);
        eventAggregator.GetEvent<MessageEvent<EquipmentDeleted>>().Subscribe(List.Apply, ThreadOption.UIThread);

        // ❌ DON'T subscribe like this:
        //
        //        string prefix = "Created: ";
        //        eventAggregator.GetEvent<MessageEvent<EquipmentCreated>>()
        //            .Subscribe(m => AddLog(prefix + m.AssetTag), ThreadOption.UIThread);
        //
        //    Prism keeps only a WEAK reference to the handler. A lambda that captures a local lives in a compiler-made
        //    closure object that nothing else references, so the next garbage collection removes it and the
        //    subscription silently stops working — no error, just no more updates, at a time you can't predict. If you
        //    need a lambda, pass keepSubscriberReferenceAlive: true and Unsubscribe it yourself when the view model
        //    goes away.
        //
        // ❌ DON'T use ThreadOption.PublisherThread and then update the UI: messages arrive on a RabbitMQ thread, and WPF
        //    throws when a bound collection or control is changed from a thread other than the UI thread.
        //
        // ❌ DON'T do slow work (HTTP calls, file IO) in a PublisherThread handler: it holds up the delivery of every
        //    message after it. Use ThreadOption.BackgroundThread, or hand the work off.

        // Connection messages from the bus, for learning purposes. They're raised on background threads.
        _bus = client.Services.GetRequiredKeyedService<RabbitMQBus>(App.BusName);
        _bus.Log += (_, message) => Application.Current?.Dispatcher.BeginInvoke(() => AddLog($"[bus] {message}"));

        // ✅ DO (re)load whenever the connection comes (back) up. This app's queue is exclusive to its connection:
        //    when the connection drops, the broker deletes the queue, and every event sent until the app reconnects is
        //    gone. The engine doesn't raise a "connected" event, so watch the flag it exposes.
        _connectionWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _connectionWatch.Tick += async (_, _) => await CheckConnectionAsync();
        _connectionWatch.Start();

        AddLaptopCommand = new DelegateCommand(async () => await RunAsync(() => _api.CreateLaptopAsync("POC 1"), "asked the API to create a laptop; waiting for the RabbitMQ event (~2 s: the outbox)"));
        DeleteSelectedCommand = new DelegateCommand(
            async () => await RunAsync(() => _api.DeleteAsync(Selected!.Id), $"asked the API to delete {Selected?.AssetTag}; waiting for the event"),
            () => Selected is not null);
        ReloadCommand = new DelegateCommand(async () => await List.LoadAsync());
    }

    public string Title { get; } = $"POC 1 — RabbitMQ via Prism IEventAggregator (pid {Environment.ProcessId})";

    public EquipmentList List { get; }

    public ObservableCollection<string> Log { get; } = [];

    public string Connection
    {
        get => _connection;
        private set => SetProperty(ref _connection, value);
    }

    public EquipmentRow? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                DeleteSelectedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public DelegateCommand AddLaptopCommand { get; }

    public DelegateCommand DeleteSelectedCommand { get; }

    public DelegateCommand ReloadCommand { get; }

    private async Task CheckConnectionAsync()
    {
        bool connected = _bus.IsConsumerConnected;
        if (connected == _wasConnected)
        {
            return;
        }

        _wasConnected = connected;
        if (connected)
        {
            Connection = "Connected to RabbitMQ, bound to the API's exchange";
            await List.LoadAsync();
        }
        else
        {
            // Also shown while waiting for the API to create its exchange: the API declares "CleanArch" the first
            // time it starts with messaging on. Subscribers only check for it; they never create it.
            Connection = "Not connected — retrying (is RabbitMQ up, and has the API started at least once?)";
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
