using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Common.RabbitMQ;
using Poc.Legacy.Events;
using Poc.Net8.Events;
using Poc.Shared;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;

namespace Poc.Net8.ViewModels;

/// <summary>
/// The equipment list, kept current by the API's events, plus notes exchanged with the other legacy apps and the
/// net472 app's disposals.
/// Everything arrives as Prism events, raised by the legacy adapter.
/// </summary>
public sealed class MainWindowViewModel : BindableBase
{
    private readonly IEventAggregator _events;
    private readonly EquipmentApi _api;
    private readonly RabbitMQService _bus;
    private readonly DispatcherTimer _connectionWatch;
    private readonly string _me = $"net8 (pid {Environment.ProcessId})";
    private bool _wasConnected;
    private string _connection = "Connecting…";
    private string _noteText = "Battery swollen, keep it off the shelf";
    private EquipmentRow? _selected;

    public MainWindowViewModel(IEventAggregator events, RabbitMQServiceRouter router, EquipmentApi api)
    {
        _events = events;
        _api = api;
        List = new EquipmentList(api);
        List.Logged += AddLog;

        // From the API. The event classes are named after the API's events (Poc.Legacy.Events/FromApi); the payloads are
        // the API's own Equipment.Messages classes.
        // ✅ DO subscribe with METHOD GROUPS on a long-lived object, and ThreadOption.UIThread to touch bound collections.
        // ❌ DON'T subscribe with a lambda that captures a local: Prism keeps a WEAK reference, and the next garbage
        //    collection silently ends the subscription.
        events.GetEvent<Equipment.EquipmentCreated>().Subscribe(List.Apply, ThreadOption.UIThread);
        events.GetEvent<Equipment.EquipmentUpdated>().Subscribe(List.Apply, ThreadOption.UIThread);
        events.GetEvent<Equipment.EquipmentDeleted>().Subscribe(List.Apply, ThreadOption.UIThread);

        // From the legacy apps: other net8 windows, the net472 app, and this window itself (PublishRemote also raises
        // the event locally; RabbitMQ never echoes it back).
        events.GetEvent<EquipmentNoteSent>().Subscribe(OnNote, ThreadOption.UIThread);
        events.GetEvent<EquipmentDisposed>().Subscribe(OnDisposed, ThreadOption.UIThread);

        _bus = router.GetBus(App.BusName);
        router.Log += (_, message) => Application.Current?.Dispatcher.InvokeAsync(() => AddLog($"[bus] {message}"));

        // ✅ DO (re)load whenever the connection comes (back) up: this app's queue is deleted with its connection, so
        //    events sent while it was down are gone.
        _connectionWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _connectionWatch.Tick += async (_, _) => await CheckConnectionAsync();
        _connectionWatch.Start();

        AddLaptopCommand = new DelegateCommand(async () => await RunAsync(() => _api.CreateLaptopAsync(_me), "asked the API to create a laptop; waiting for its event"));
        DeleteSelectedCommand = new DelegateCommand(
            async () => await RunAsync(() => _api.DeleteAsync(Selected!.Id), $"asked the API to delete {Selected?.AssetTag}; waiting for its event"),
            () => Selected is not null);
        ReloadCommand = new DelegateCommand(async () => await List.LoadAsync());
        SendNoteCommand = new DelegateCommand(SendNote, () => Selected is not null);
    }

    public string Title => $"net8 — migrated legacy app, legacy event format ({_me})";

    public EquipmentList List { get; }

    public ObservableCollection<string> Log { get; } = [];

    public string Connection
    {
        get => _connection;
        private set => SetProperty(ref _connection, value);
    }

    public string NoteText
    {
        get => _noteText;
        set => SetProperty(ref _noteText, value);
    }

    public EquipmentRow? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                DeleteSelectedCommand.RaiseCanExecuteChanged();
                SendNoteCommand.RaiseCanExecuteChanged();

                // Local only: Publish, not PublishRemote. Whoever in this app cares (the status bar) hears it;
                // no other app does. See Events/EquipmentSelected.cs for what keeps it off the broker.
                _events.GetEvent<EquipmentSelected>().Publish(value);
            }
        }
    }

    public DelegateCommand AddLaptopCommand { get; }

    public DelegateCommand DeleteSelectedCommand { get; }

    public DelegateCommand ReloadCommand { get; }

    public DelegateCommand SendNoteCommand { get; }

    private void SendNote()
    {
        // App to app, the legacy way. Buffered while disconnected, sent once connected.
        _events.GetEvent<EquipmentNoteSent>().PublishRemote(new EquipmentNote
        {
            EquipmentId = Selected!.Id,
            AssetTag = Selected.AssetTag,
            Text = NoteText,
            From = _me,
        });
    }

    private void OnNote(EquipmentNote note)
    {
        AddLog($"note on {note.AssetTag} from {(note.From == _me ? "me" : note.From)}: {note.Text}");
    }

    private void OnDisposed(EquipmentDisposal disposal)
    {
        // Sent by the net472 app to the API; this app only hears about it. The row goes when the API's
        // EquipmentDeleted arrives.
        AddLog($"{disposal.DisposedBy} disposed of {disposal.AssetTag}; the API will delete it");
    }

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
            Connection = "Connected: bound to CleanArch (API), Poc.Net8 (own) and Poc.Net472";
            await List.LoadAsync();
        }
        else
        {
            // A subscriber never creates another app's exchange; it waits for it (setup-exchanges.ps1 creates them all).
            Connection = "Not connected — retrying (is RabbitMQ up, and do the exchanges exist?)";
        }
    }

    private async Task RunAsync(Func<Task> call, string description)
    {
        try
        {
            await call();
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
