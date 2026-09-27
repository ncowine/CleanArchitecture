using System.Net;
using System.Net.Http;
using Equipment.Messages;
using Microsoft.AspNetCore.SignalR.Client;
using Poc.Shared;

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
                // ✅ DO authenticate the hub connection with the same key as the HTTP calls. The hub has [Authorize], so
                //    without a valid key the negotiate request gets 401 and no connection is made. The .NET client sends
                //    these headers on the negotiate request and on the WebSocket itself.
                http.Headers["X-Api-Key"] = options.ApiKey;
            })
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            .Build();

        // ✅ DO register handlers BEFORE StartAsync, so nothing sent right after connecting is missed.
        // The method names are the server's event names (RealtimeEvent.Type, = the class name), matched exactly.
        // The payloads are the API's Equipment.Messages classes — the SAME ones RabbitMQ carries to POC 1 and 2 — so
        // there is one contract per event, whichever way a client listens.
        _connection.On<EquipmentCreated>(nameof(EquipmentCreated), e => Created?.Invoke(e));
        _connection.On<EquipmentUpdated>(nameof(EquipmentUpdated), e => Updated?.Invoke(e));
        _connection.On<EquipmentDeleted>(nameof(EquipmentDeleted), e => Deleted?.Invoke(e));
        // Presence lists distinct USER NAMES, not connections: the authenticated caller's name, which for an API key is
        // the key's subject. Every window using the same key counts as one user.
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

    public event Action<EquipmentCreated>? Created;

    public event Action<EquipmentUpdated>? Updated;

    public event Action<EquipmentDeleted>? Deleted;

    public event Action<int>? PresenceChanged;

    public event Action<string>? StatusChanged;

    /// <summary>After an automatic reconnect: the group has been joined again, and state must be reloaded.</summary>
    public event Action? Reconnected;

    /// <summary>Connects (retrying until the API is reachable) and joins the equipment group.</summary>
    /// <returns>False when the API rejected the credentials: retrying won't help, so it stops.</returns>
    public async Task<bool> ConnectAsync(CancellationToken cancellationToken)
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
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                // ❌ DON'T retry a rejected credential forever. An unreachable API comes back by itself; a wrong or
                //    revoked key doesn't. Stop, and say what to fix.
                StatusChanged?.Invoke($"The API rejected the API key ({(int)ex.StatusCode.Value}). Check Api:ApiKey in appsettings.json.");
                return false;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                StatusChanged?.Invoke($"API not reachable ({ex.Message}). Retrying in 5 s…");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }

        await JoinAsync();
        StatusChanged?.Invoke("Connected");
        return true;
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    private Task JoinAsync() => _connection.InvokeAsync("JoinGroup", EquipmentGroup);

    /// <summary>The hub's "presence" message: who is watching a group.</summary>
    private sealed record PresenceDto(string Group, List<string> Users);
}
