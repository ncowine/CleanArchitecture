using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Equipment.Messages;
using Messaging.Hosting;
using Messaging.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using Poc.Shared;

namespace Poc2.PlainRabbitMq.ViewModels;

/// <summary>
/// The equipment list, kept current by the API's RabbitMQ events, received through <see cref="IMessageSubscriber"/>.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly List<IDisposable> _subscriptions = [];
    private readonly SynchronizationContext _ui;
    private readonly EquipmentApi _api;
    private readonly RabbitMQBus _bus;
    private readonly DispatcherTimer _connectionWatch;
    private bool _wasConnected;

    [ObservableProperty]
    private string _connection = "Connecting…";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private EquipmentRow? _selected;

    public MainWindowViewModel(IMessageSubscriber subscriber, IServiceProvider services, EquipmentApi api)
    {
        _api = api;
        List = new EquipmentList(api);
        List.Logged += AddLog;

        // Created by the app on the UI thread, so this is WPF's dispatcher context.
        // ❌ DON'T create a view model on a background thread (e.g. inside Task.Run) and read Current there: it's null,
        //    and handlers would then run on a RabbitMQ thread and crash the moment they touch the UI.
        _ui = SynchronizationContext.Current
            ?? throw new InvalidOperationException("Create the view model on the UI thread.");

        // ✅ DO pass the UI's SynchronizationContext: each handler is posted to the UI thread, like Prism's
        //    ThreadOption.UIThread, so it may update bound collections directly.
        // ✅ DO keep the returned IDisposable and dispose it when this object goes away (see Dispose below).
        //    Unlike Prism, these are STRONG references: a window that subscribes and is closed without disposing stays
        //    in memory and keeps handling messages.
        _subscriptions.Add(subscriber.Subscribe<EquipmentCreated>(List.Apply, _ui));
        _subscriptions.Add(subscriber.Subscribe<EquipmentUpdated>(List.Apply, _ui));
        _subscriptions.Add(subscriber.Subscribe<EquipmentDeleted>(List.Apply, _ui));

        // The other overload runs on the RabbitMQ thread and may be async:
        //
        //     subscriber.Subscribe<EquipmentCreated>(async (message, context, token) => { ... });
        //
        // ✅ Use it for non-UI work that needs the MessageContext (message-id, correlation-id, headers). The API sends
        //    its outbox row's id as the message-id and the originating request's correlation id: log them and you can
        //    follow one change from the HTTP request into this app.
        // ❌ DON'T touch the UI inside it, and DON'T do long work there: it holds up every later message.
        // ❌ Its exceptions fail the message; on a desktop app's queue that means logged and dropped, not retried.
        //    Exceptions from the UI-thread overload go to Application.DispatcherUnhandledException instead.

        // Connection messages from the bus, for learning purposes. They're raised on background threads.
        _bus = services.GetRequiredKeyedService<RabbitMQBus>(App.BusName);
        _bus.Log += (_, message) => _ui.Post(_ => AddLog($"[bus] {message}"), null);

        // ✅ DO (re)load whenever the connection comes (back) up: this app's queue is deleted when its connection drops,
        //    so events sent in the meantime are gone (see POC 1).
        _connectionWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _connectionWatch.Tick += async (_, _) => await CheckConnectionAsync();
        _connectionWatch.Start();
    }

    public string Title { get; } = $"POC 2 — RabbitMQ without IEventAggregator (pid {Environment.ProcessId})";

    public EquipmentList List { get; }

    public ObservableCollection<string> Log { get; } = [];

    public void Dispose()
    {
        _connectionWatch.Stop();
        foreach (IDisposable subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    [RelayCommand]
    private Task AddLaptopAsync() =>
        RunAsync(() => _api.CreateLaptopAsync("POC 2"), "asked the API to create a laptop; waiting for the RabbitMQ event (~2 s: the outbox)");

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private Task DeleteSelectedAsync() =>
        RunAsync(() => _api.DeleteAsync(Selected!.Id), $"asked the API to delete {Selected?.AssetTag}; waiting for the event");

    private bool CanDeleteSelected() => Selected is not null;

    [RelayCommand]
    private Task ReloadAsync() => List.LoadAsync();

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
