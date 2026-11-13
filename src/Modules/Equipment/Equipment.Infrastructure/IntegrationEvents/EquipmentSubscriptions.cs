using Messaging;
using Messaging.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Equipment.Infrastructure.IntegrationEvents;

/// <summary>
/// Subscribes Equipment to the integration events it receives from other applications. Each message gets its own DI
/// scope, so the handlers can use scoped services such as the DbContext. Without messaging configured there is no
/// <see cref="IMessageSubscriber"/> and this does nothing.
/// </summary>
/// <remarks>
/// Subscribing in <see cref="StartAsync"/> is in time: the buses start after every hosted service has started, so the
/// API's shared queue never drops a waiting message for lack of a subscriber.
/// </remarks>
internal sealed class EquipmentSubscriptions : IHostedService
{
    private readonly IServiceProvider _provider;
    private readonly List<IDisposable> _subscriptions = new();

    public EquipmentSubscriptions(IServiceProvider provider)
    {
        _provider = provider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_provider.GetService<IMessageSubscriber>() is not { } subscriber)
        {
            return Task.CompletedTask;
        }

        // The net472 POC sends disposals with the "equipment" routing key, so this binds that key on its exchange.
        _subscriptions.Add(subscriber.Subscribe<EquipmentDisposed>(
            (message, context, token) => InScopeAsync<EquipmentDisposedHandler>(handler => handler.HandleAsync(message, context, token)),
            new SubscribeOptions { Exchange = "Poc.Net472", RoutingKey = "equipment" }));

        _subscriptions.Add(subscriber.Subscribe<SiteRenamed>(
            (message, context, token) => InScopeAsync<SiteRenamedHandler>(handler => handler.HandleAsync(message, context, token))));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (IDisposable subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        return Task.CompletedTask;
    }

    private async Task InScopeAsync<THandler>(Func<THandler, Task> handle)
        where THandler : notnull
    {
        await using AsyncServiceScope scope = _provider.CreateAsyncScope();
        await handle(scope.ServiceProvider.GetRequiredService<THandler>());
    }
}
