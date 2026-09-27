using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Messaging.Hosting;
using Messaging.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using Poc.Contracts;

namespace Poc2.PlainRabbitMq.ViewModels;

/// <summary>
/// Receives <see cref="EquipmentStatusChanged"/> through <see cref="IMessageSubscriber"/> and shows the latest status per
/// asset.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly List<IDisposable> _subscriptions = [];
    private readonly SynchronizationContext _ui;

    [ObservableProperty]
    private string _connection = "Connecting…";

    public MainWindowViewModel(IMessageSubscriber subscriber, IServiceProvider services)
    {
        // Created by the app on the UI thread, so this is WPF's dispatcher context.
        // ❌ DON'T create a view model on a background thread (e.g. inside Task.Run) and read Current there: it's null,
        //    and handlers would then run on a RabbitMQ thread and crash the moment they touch the UI.
        _ui = SynchronizationContext.Current
            ?? throw new InvalidOperationException("Create the view model on the UI thread.");

        // ✅ DO pass the UI's SynchronizationContext: the handler is posted to the UI thread, like Prism's
        //    ThreadOption.UIThread, so it may update bound collections directly.
        // ✅ DO keep the returned IDisposable and dispose it when this object goes away (see Dispose below).
        //    Unlike Prism, these are STRONG references: a window that subscribes and is closed without disposing stays
        //    in memory and keeps handling messages.
        _subscriptions.Add(subscriber.Subscribe<EquipmentStatusChanged>(OnStatusChanged, _ui));

        // The other overload runs on the RabbitMQ thread and may be async:
        //
        //     subscriber.Subscribe<EquipmentStatusChanged>(async (message, context, token) => { ... });
        //
        // ✅ Use it for non-UI work that needs the MessageContext (message-id, correlation-id, headers).
        // ❌ DON'T touch the UI inside it, and DON'T do long work there: it holds up every later message.
        // ❌ Its exceptions fail the message; on a desktop app's queue that means logged and dropped, not retried.
        //    Exceptions from the UI-thread overload go to Application.DispatcherUnhandledException instead.

        // Connection messages from the bus, for learning purposes. They're raised on background threads.
        RabbitMQBus bus = services.GetRequiredKeyedService<RabbitMQBus>(App.BusName);
        bus.Log += (_, message) => _ui.Post(_ => OnBusLog(message), null);

        // ✅ DO load the current state from the API at startup (not shown: this POC has no API endpoint for it).
        //    A desktop app's queue exists only while the app runs, so events sent while it was closed are gone.
    }

    public string Title { get; } = $"POC 2 — RabbitMQ without IEventAggregator (pid {Environment.ProcessId})";

    public ObservableCollection<AssetStatusRow> Assets { get; } = [];

    public ObservableCollection<string> Log { get; } = [];

    public void Dispose()
    {
        foreach (IDisposable subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    private void OnStatusChanged(EquipmentStatusChanged message)
    {
        // ✅ DO make applying an event safe to repeat, and ignore one older than what's shown (see POC 1 for why).
        AssetStatusRow? row = Assets.FirstOrDefault(a => a.EquipmentId == message.EquipmentId);
        if (row is null)
        {
            Assets.Add(new AssetStatusRow(message.EquipmentId, message.AssetTag, message.Status, message.ChangedAtUtc));
        }
        else if (message.ChangedAtUtc >= row.ChangedAtUtc)
        {
            Assets[Assets.IndexOf(row)] = row with { Status = message.Status, ChangedAtUtc = message.ChangedAtUtc };
        }

        AddLog($"EquipmentStatusChanged  {message.AssetTag} → {message.Status}");
    }

    private void OnBusLog(string message)
    {
        if (message.StartsWith("Consumer connected", StringComparison.Ordinal))
        {
            Connection = "Connected";
        }
        else if (message.Contains("does not exist yet", StringComparison.Ordinal))
        {
            Connection = "Waiting for the server to create its exchange (start Poc.ServerSimulator)";
        }
        else if (message.Contains("failed", StringComparison.OrdinalIgnoreCase) || message.Contains("lost", StringComparison.OrdinalIgnoreCase))
        {
            Connection = "Disconnected — retrying";
        }

        AddLog($"[bus] {message}");
    }

    private void AddLog(string line)
    {
        Log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {line}");
    }
}

public sealed record AssetStatusRow(Guid EquipmentId, string AssetTag, string Status, DateTime ChangedAtUtc);
