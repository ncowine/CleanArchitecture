using System.Collections.ObjectModel;
using System.Windows;
using Messaging.Hosting;
using Messaging.Prism;
using Messaging.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using Poc.Contracts;
using Prism.Events;
using Prism.Mvvm;

namespace Poc1.PrismRabbitMq.ViewModels;

/// <summary>
/// Receives <see cref="EquipmentStatusChanged"/> as a Prism event and shows the latest status per asset.
/// </summary>
public sealed class MainWindowViewModel : BindableBase
{
    private string _connection = "Connecting…";

    public MainWindowViewModel(IEventAggregator eventAggregator, MessagingClient client)
    {
        // ✅ DO subscribe with a METHOD GROUP on a long-lived object, and ThreadOption.UIThread so the handler may touch
        //    bound collections directly.
        eventAggregator.GetEvent<MessageEvent<EquipmentStatusChanged>>()
            .Subscribe(OnStatusChanged, ThreadOption.UIThread);

        // ❌ DON'T subscribe like this:
        //
        //        string prefix = "Changed: ";
        //        eventAggregator.GetEvent<MessageEvent<EquipmentStatusChanged>>()
        //            .Subscribe(m => Log(prefix + m.AssetTag), ThreadOption.UIThread);
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
        RabbitMQBus bus = client.Services.GetRequiredKeyedService<RabbitMQBus>(App.BusName);
        bus.Log += (_, message) => Application.Current?.Dispatcher.BeginInvoke(() => OnBusLog(message));

        // ✅ DO load the current state from the API when the app starts (not shown: this POC has no API endpoint for
        //    it). A desktop app's queue exists only while the app runs: events sent while it was closed are NOT waiting
        //    for it. Load first, then let events keep the screen current.
    }

    public string Title { get; } = $"POC 1 — RabbitMQ via Prism IEventAggregator (pid {Environment.ProcessId})";

    public string Connection
    {
        get => _connection;
        private set => SetProperty(ref _connection, value);
    }

    /// <summary>Latest known status per asset: what a real screen would show.</summary>
    public ObservableCollection<AssetStatusRow> Assets { get; } = [];

    /// <summary>Every message and connection event, newest first.</summary>
    public ObservableCollection<string> Log { get; } = [];

    private void OnStatusChanged(EquipmentStatusChanged message)
    {
        // ✅ DO apply events so that applying one twice does no harm ("set status to X", not "advance status"):
        //    delivery is at least once, so a duplicate can arrive.
        AssetStatusRow? row = Assets.FirstOrDefault(a => a.EquipmentId == message.EquipmentId);
        if (row is null)
        {
            Assets.Add(new AssetStatusRow(message.EquipmentId, message.AssetTag, message.Status, message.ChangedAtUtc));
        }
        else if (message.ChangedAtUtc >= row.ChangedAtUtc)
        {
            // ✅ DO ignore an event older than what you're showing: after a reconnect, or when the server retries,
            //    an older event can arrive after a newer one.
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
            // The subscribed exchange belongs to the server. Until the server has started once, it doesn't exist, and
            // this app waits and retries. That's normal, not an error.
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
