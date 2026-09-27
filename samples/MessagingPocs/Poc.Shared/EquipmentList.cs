using System.Collections.ObjectModel;
using Equipment.Messages;

namespace Poc.Shared;

/// <summary>
/// Keeps a list of equipment current: load it over HTTP, then apply the API's events as they arrive. The same rules
/// apply whichever transport delivers the events, so all three POCs use this.
/// </summary>
/// <remarks>
/// Call everything on the UI thread. That also serialises events: they're applied one at a time, in arrival order.
/// </remarks>
public sealed class EquipmentList
{
    private readonly EquipmentApi _api;

    // Events that arrive while a load is in flight wait here, and are applied after it.
    private readonly List<Action> _arrivedDuringLoad = [];
    private bool _loading = true;   // until the first load: events can arrive between subscribing and loading

    public EquipmentList(EquipmentApi api)
    {
        _api = api;
    }

    public ObservableCollection<EquipmentRow> Items { get; } = [];

    /// <summary>A line for the app's log, e.g. "loaded 3 item(s)".</summary>
    public event Action<string>? Logged;

    /// <summary>
    /// Loads the whole list over HTTP.
    /// ✅ DO call this when the app starts AND every time the connection comes back. Neither RabbitMQ (a desktop app's
    ///    queue disappears when it disconnects) nor SignalR (no queue at all) keeps events for an absent client.
    /// ✅ DO subscribe/connect BEFORE the first load. The other order leaves a gap: a change made after the load but
    ///    before subscribing is in neither the list nor the events.
    /// </summary>
    public async Task LoadAsync()
    {
        // Events arriving during the load are held back and applied afterwards, in order. Applied before the load
        // finished, they'd be wiped out when the (possibly older) list replaced Items. Applied after, they're safe:
        // each one SETS a state ("row X now looks like this") rather than changing it.
        _loading = true;
        try
        {
            List<EquipmentRow> all = await _api.LoadAllAsync();

            Items.Clear();
            foreach (EquipmentRow row in all)
            {
                Items.Add(row);
            }

            foreach (Action apply in _arrivedDuringLoad)
            {
                apply();
            }

            Logged?.Invoke($"loaded {all.Count} item(s) over HTTP" +
                (_arrivedDuringLoad.Count > 0 ? $", then applied {_arrivedDuringLoad.Count} event(s) that arrived meanwhile" : ""));
        }
        catch (HttpRequestException ex)
        {
            Logged?.Invoke($"load failed (is the API running?): {ex.Message}");
        }
        finally
        {
            _arrivedDuringLoad.Clear();
            _loading = false;
        }
    }

    // ✅ DO treat Created and Updated the same way — "this row now looks like this" — so a repeated or out-of-order
    //    event is harmless. RabbitMQ delivers at least once; after a reconnect, an event can also repeat what the reload
    //    already showed.
    public void Apply(EquipmentCreated e) =>
        OnEvent(() => Upsert(new EquipmentRow(e.Id, e.Name, e.Category, e.AssetTag, e.Status)), $"EquipmentCreated  {e.AssetTag}");

    public void Apply(EquipmentUpdated e) =>
        OnEvent(() => Upsert(new EquipmentRow(e.Id, e.Name, e.Category, e.AssetTag, e.Status)), $"EquipmentUpdated  {e.AssetTag}");

    // Deleting something that isn't there is also harmless.
    public void Apply(EquipmentDeleted e) =>
        OnEvent(() => Remove(e.Id), $"EquipmentDeleted  {e.Id}");

    private void OnEvent(Action apply, string description)
    {
        Logged?.Invoke($"event: {description}");
        if (_loading)
        {
            _arrivedDuringLoad.Add(apply);
        }
        else
        {
            apply();
        }
    }

    private void Upsert(EquipmentRow row)
    {
        int index = IndexOf(row.Id);
        if (index < 0)
        {
            Items.Add(row);
        }
        else
        {
            Items[index] = row;
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
}
