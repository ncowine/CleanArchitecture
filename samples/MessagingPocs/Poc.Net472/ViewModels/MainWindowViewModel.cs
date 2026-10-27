using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Common.RabbitMQ;
using Poc.Legacy.Events;
using Poc.Net472.Events;
using Poc.Shared;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;

namespace Poc.Net472.ViewModels;

/// <summary>
/// The equipment list, kept current by the API's events, plus notes and disposals exchanged with the other legacy apps.
/// Everything arrives as Prism events, raised by the legacy adapter.
/// </summary>
public sealed class MainWindowViewModel : BindableBase
{
    private readonly IEventAggregator events;
    private readonly EquipmentApi api;
    private readonly RabbitMQService bus;
    private readonly DispatcherTimer connectionWatch;
    private readonly string me = $"net472 (pid {Process.GetCurrentProcess().Id})";
    private bool wasConnected;
    private string connection = "Connecting…";
    private string noteText = "Battery swollen, keep it off the shelf";
    private EquipmentRow? selected;

    public MainWindowViewModel(IEventAggregator events, RabbitMQServiceRouter router, EquipmentApi api)
    {
        this.events = events;
        this.api = api;
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

        // From the legacy apps: other net472 windows, the net8 app, and this window itself (PublishRemote also raises
        // the event locally; RabbitMQ never echoes it back).
        events.GetEvent<EquipmentNoteSent>().Subscribe(OnNote, ThreadOption.UIThread);
        events.GetEvent<EquipmentDisposed>().Subscribe(OnDisposed, ThreadOption.UIThread);

        bus = router.GetBus(App.BusName);
        router.Log += (sender, message) => Application.Current?.Dispatcher.InvokeAsync(() => AddLog($"[bus] {message}"));

        // ✅ DO (re)load whenever the connection comes (back) up: this app's queue is deleted with its connection, so
        //    events sent while it was down are gone.
        connectionWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        connectionWatch.Tick += async (sender, e) => await CheckConnectionAsync();
        connectionWatch.Start();

        AddLaptopCommand = new DelegateCommand(async () => await RunAsync(() => api.CreateLaptopAsync(me), "asked the API to create a laptop; waiting for its event"));
        DeleteSelectedCommand = new DelegateCommand(
            async () => await RunAsync(() => api.DeleteAsync(Selected!.Id), $"asked the API to delete {Selected?.AssetTag}; waiting for its event"),
            () => Selected is not null);
        ReloadCommand = new DelegateCommand(async () => await List.LoadAsync());
        SendNoteCommand = new DelegateCommand(SendNote, () => Selected is not null);
        DisposeSelectedCommand = new DelegateCommand(DisposeSelected, () => Selected is not null);
    }

    public string Title => $"net472 — legacy app, legacy event format ({me})";

    public EquipmentList List { get; }

    public ObservableCollection<string> Log { get; } = [];

    public string Connection
    {
        get => connection;
        private set => SetProperty(ref connection, value);
    }

    public string NoteText
    {
        get => noteText;
        set => SetProperty(ref noteText, value);
    }

    public EquipmentRow? Selected
    {
        get => selected;
        set
        {
            if (SetProperty(ref selected, value))
            {
                DeleteSelectedCommand.RaiseCanExecuteChanged();
                SendNoteCommand.RaiseCanExecuteChanged();
                DisposeSelectedCommand.RaiseCanExecuteChanged();

                // Local only: Publish, not PublishRemote. Whoever in this app cares (the status bar) hears it;
                // no other app does. See Events/EquipmentSelected.cs for what keeps it off the broker.
                events.GetEvent<EquipmentSelected>().Publish(value);
            }
        }
    }

    public DelegateCommand AddLaptopCommand { get; }

    public DelegateCommand DeleteSelectedCommand { get; }

    public DelegateCommand ReloadCommand { get; }

    public DelegateCommand SendNoteCommand { get; }

    public DelegateCommand DisposeSelectedCommand { get; }

    private void SendNote()
    {
        // App to app, the legacy way. Buffered while disconnected, sent once connected.
        events.GetEvent<EquipmentNoteSent>().PublishRemote(new EquipmentNote
        {
            EquipmentId = Selected!.Id,
            AssetTag = Selected.AssetTag,
            Text = NoteText,
            From = me,
        });
    }

    private void DisposeSelected()
    {
        // The API handles this one: it deletes the equipment, and its EquipmentDeleted (~2 s later, from its outbox)
        // removes the row in every window, this one included.
        // ❌ DON'T remove the row here as well; the API's event does it, for everyone alike.
        events.GetEvent<EquipmentDisposed>().PublishRemote(new EquipmentDisposal
        {
            EquipmentId = Selected!.Id,
            AssetTag = Selected.AssetTag,
            DisposedBy = me,
        });
    }

    private void OnNote(EquipmentNote note)
    {
        AddLog($"note on {note.AssetTag} from {(note.From == me ? "me" : note.From)}: {note.Text}");
    }

    private void OnDisposed(EquipmentDisposal disposal)
    {
        AddLog(disposal.DisposedBy == me
            ? $"sent EquipmentDisposed for {disposal.AssetTag}; the API will delete it"
            : $"{disposal.DisposedBy} disposed of {disposal.AssetTag}");
    }

    private async Task CheckConnectionAsync()
    {
        bool connected = bus.IsConsumerConnected;
        if (connected == wasConnected)
        {
            return;
        }

        wasConnected = connected;
        if (connected)
        {
            Connection = "Connected: bound to CleanArch (API), Poc.Net472 (own) and Poc.Net8";
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
