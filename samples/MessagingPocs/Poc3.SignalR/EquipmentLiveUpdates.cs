using Microsoft.AspNetCore.SignalR.Client;

namespace Poc3.SignalR;

/// <summary>
/// The SignalR connection to the API's hub (<c>/hubs/presence</c>): connects, keeps reconnecting, joins the equipment
/// group, and turns the hub's messages into .NET events.
/// </summary>
/// <remarks>
/// Every event here is raised on a thread-pool thread. The view model moves them to the UI thread.
/// </remarks>
public sealed class EquipmentLiveUpdates : IAsyncDisposable
{
    /// <summary>The group name the API publishes equipment changes to (RealtimeGroups.Equipment() on the server).</summary>
    private const string EquipmentGroup = "equipment";

    private readonly HubConnection _connection;

    public EquipmentLiveUpdates(ApiOptions options)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(options.BaseUrl), "hubs/presence"), http =>
            {
                // ✅ DO authenticate the hub connection with the same key as the HTTP calls. The .NET client sends these
                //    headers on the negotiate request and on the WebSocket itself.
                //    ⚠ Today the API's hub doesn't require authentication (PresenceHub has no [Authorize]), so this
                //    header isn't checked yet. Anyone who can reach the API can listen. That's on the server to fix.
                http.Headers["X-Api-Key"] = options.ApiKey;
            })
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            .Build();

        // ✅ DO register handlers BEFORE StartAsync, so nothing sent right after connecting is missed.
        // The method names are the server's event names (RealtimeEvent.Type), matched exactly.
        _connection.On<EquipmentDto>("EquipmentCreated", dto => EquipmentChanged?.Invoke(dto));
        _connection.On<EquipmentDto>("EquipmentUpdated", dto => EquipmentChanged?.Invoke(dto));
        _connection.On<EquipmentDeletedDto>("EquipmentDeleted", dto => EquipmentDeleted?.Invoke(dto.Id));
        // Presence lists distinct USER NAMES, not connections. While the hub is anonymous every client is "anonymous",
        // so this shows 1 however many windows are open. It becomes meaningful once the hub authenticates callers.
        _connection.On<PresenceDto>("presence", dto => PresenceChanged?.Invoke(dto.Users.Count));

        _connection.Reconnecting += error =>
        {
            StatusChanged?.Invoke("Connection lost — reconnecting…");
            return Task.CompletedTask;
        };

        _connection.Reconnected += async connectionId =>
        {
            // ❌ DON'T assume you're still in your groups after a reconnect. A reconnect is a NEW connection with a new
            //    id, and group membership belonged to the old one. Without joining again, the app looks connected and
            //    never receives another event. It's the most common SignalR bug.
            await JoinAsync();
            StatusChanged?.Invoke("Connected");

            // Everything sent while disconnected is gone: SignalR doesn't queue for absent clients. Tell the view
            // model to reload.
            Reconnected?.Invoke();
        };

        _connection.Closed += error =>
        {
            // With ForeverRetryPolicy this only happens when the app disposes the connection.
            StatusChanged?.Invoke("Disconnected");
            return Task.CompletedTask;
        };
    }

    public event Action<EquipmentDto>? EquipmentChanged;

    public event Action<Guid>? EquipmentDeleted;

    public event Action<int>? PresenceChanged;

    public event Action<string>? StatusChanged;

    /// <summary>After an automatic reconnect: the group has been joined again, and state must be reloaded.</summary>
    public event Action? Reconnected;

    /// <summary>Connects (retrying until the API is reachable) and joins the equipment group.</summary>
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        // ❌ DON'T expect WithAutomaticReconnect to cover the FIRST connection. It only reconnects a connection that was
        //    once up. If the API isn't running when the app starts, StartAsync throws, and you have to retry yourself.
        while (true)
        {
            try
            {
                StatusChanged?.Invoke("Connecting…");
                await _connection.StartAsync(cancellationToken);
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                StatusChanged?.Invoke($"API not reachable ({ex.Message}). Retrying in 5 s…");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }

        await JoinAsync();
        StatusChanged?.Invoke("Connected");
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    private Task JoinAsync() => _connection.InvokeAsync("JoinGroup", EquipmentGroup);
}
