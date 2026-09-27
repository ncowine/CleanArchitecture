using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Poc3.SignalR.ViewModels;

/// <summary>
/// The equipment list, kept current by SignalR. Loads over HTTP, listens over SignalR, changes things over HTTP.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly EquipmentApi _api;
    private readonly EquipmentLiveUpdates _live;
    private readonly SynchronizationContext _ui;

    // Events that arrive while a reload is in flight wait here, and are applied after it (see ReloadAsync).
    private readonly List<Action> _arrivedDuringLoad = [];
    private bool _loading;

    [ObservableProperty]
    private string _connection = "Starting…";

    [ObservableProperty]
    private int _watchers;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private EquipmentDto? _selected;

    public MainWindowViewModel(EquipmentApi api, EquipmentLiveUpdates live)
    {
        _api = api;
        _live = live;
        _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("Create the view model on the UI thread.");

        // SignalR raises these on thread-pool threads.
        // ❌ DON'T update Items straight from them: WPF throws when a bound collection changes off the UI thread.
        // ✅ DO post every one to the UI thread. That also serialises them: they're handled one at a time, in order.
        _live.EquipmentChanged += dto => _ui.Post(_ => OnEvent(() => Upsert(dto), $"changed  {dto.AssetTag} ({dto.Status})"), null);
        _live.EquipmentDeleted += id => _ui.Post(_ => OnEvent(() => Remove(id), $"deleted  {id}"), null);
        _live.PresenceChanged += count => _ui.Post(_ => Watchers = count, null);
        _live.StatusChanged += status => _ui.Post(_ => Connection = status, null);
        _live.Reconnected += () => _ui.Post(async _ => await ReloadAsync(), null);
    }

    public string Title { get; } = $"POC 3 — SignalR from the API (pid {Environment.ProcessId})";

    public ObservableCollection<EquipmentDto> Items { get; } = [];

    public ObservableCollection<string> Log { get; } = [];

    /// <summary>Called once the window is showing.</summary>
    public async Task StartAsync()
    {
        // ✅ DO connect and join FIRST, then load. The other order leaves a gap: a change made after the load but
        //    before the join is in neither the list nor the events, and the screen stays wrong until the next change.
        //    Events that arrive between the join and the load are held back like any during a load.
        _loading = true;
        await _live.ConnectAsync(CancellationToken.None);
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        // The events that arrive during the load are held back and applied afterwards, in order. Applying them before
        // the load finished would be undone when the (possibly older) list replaced Items. Applying them after is
        // safe because each one sets a state rather than changing it ("row X now looks like this").
        _loading = true;
        try
        {
            List<EquipmentDto> all = await _api.LoadAllAsync();

            Items.Clear();
            foreach (EquipmentDto dto in all)
            {
                Items.Add(dto);
            }

            foreach (Action apply in _arrivedDuringLoad)
            {
                apply();
            }

            AddLog($"loaded {all.Count} item(s) over HTTP{(_arrivedDuringLoad.Count > 0 ? $", then applied {_arrivedDuringLoad.Count} event(s) that arrived meanwhile" : "")}");
        }
        catch (HttpRequestException ex)
        {
            AddLog($"load failed: {ex.Message}");
        }
        finally
        {
            _arrivedDuringLoad.Clear();
            _loading = false;
        }
    }

    [RelayCommand]
    private async Task AddLaptopAsync()
    {
        try
        {
            await _api.CreateLaptopAsync();

            // ❌ DON'T add the new row to Items here as well. The API pushes EquipmentCreated to everyone in the group,
            //    this window included, and that's what adds it. Adding it here too shows it twice, or makes you write
            //    "ignore my own echo" logic that goes wrong the first time two windows are open.
            AddLog("asked the API to create a laptop (HTTP 201); waiting for the event");
        }
        catch (HttpRequestException ex)
        {
            AddLog($"create failed: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelectedAsync()
    {
        if (Selected is not { } item)
        {
            return;
        }

        try
        {
            await _api.DeleteAsync(item.Id);
            AddLog($"asked the API to delete {item.AssetTag}; waiting for the event");
        }
        catch (HttpRequestException ex)
        {
            AddLog($"delete failed: {ex.Message}");
        }
    }

    private bool CanDeleteSelected() => Selected is not null;

    private void OnEvent(Action apply, string description)
    {
        AddLog($"event: {description}");
        if (_loading)
        {
            _arrivedDuringLoad.Add(apply);
        }
        else
        {
            apply();
        }
    }

    /// <summary>
    /// Created and Updated both mean "this row now looks like this". Treating them the same makes a repeated or
    /// out-of-order event harmless.
    /// </summary>
    private void Upsert(EquipmentDto dto)
    {
        int index = IndexOf(dto.Id);
        if (index < 0)
        {
            Items.Add(dto);
        }
        else
        {
            Items[index] = dto;
        }
    }

    private void Remove(Guid id)
    {
        int index = IndexOf(id);
        if (index >= 0)
        {
            Items.RemoveAt(index);
        }
    }

    private int IndexOf(Guid id)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private void AddLog(string line)
    {
        Log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {line}");
    }
}
