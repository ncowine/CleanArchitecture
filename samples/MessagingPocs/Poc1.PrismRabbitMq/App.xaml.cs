using System.Windows;
using Messaging.Hosting;
using Messaging.Prism;
using Microsoft.Extensions.Configuration;
using Poc.Contracts;
using Poc1.PrismRabbitMq.Views;
using Prism.DryIoc;
using Prism.Events;
using Prism.Ioc;

namespace Poc1.PrismRabbitMq;

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
// POC 1 — RabbitMQ through Prism's IEventAggregator
//
// WHEN THIS IS THE RIGHT CHOICE
//   An app being moved from .NET Framework to .NET 8 that is built around Prism and IEventAggregator, and needs to
//   keep receiving broker messages while it's migrated. View models keep the Prism style they already have.
//   For a NEW app, prefer POC 3 (SignalR); for a new app that must use the broker, POC 2 has fewer moving parts.
//
// HOW IT'S WIRED
//   MessagingClient (Messaging.Hosting) owns the RabbitMQ connection. UseEventAggregator (Messaging.Prism) turns each
//   received message into GetEvent<MessageEvent<T>>().Publish(message), so view models subscribe the Prism way.
//
// ✅ DO   use plain message classes with [Message] and MessageEvent<T> (Messaging.Prism).
// ❌ DON'T reference Common.RabbitMQ or declare PubSubEvent<T> subclasses as messages in a new app. That is the legacy
//         adapter: it exists so .NET Framework apps keep working unchanged, and new code built on it inherits their
//         limits (Newtonsoft 12, Prism-typed contracts, events found by assembly scanning).
// ❌ DON'T publish from here. Modern apps only listen: changes go to the API over HTTP, and the server publishes.
//         Messaging.Prism offers PublishRemote/PublishRemoteTo on MessageEvent<T>; don't call them, and don't add a
//         Route<T>() — without a route there is nothing this app can publish.
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
public partial class App : PrismApplication
{
    public const string BusName = "Main";

    protected override Window CreateShell()
    {
        // Resolving the window builds its view model, which subscribes to the Prism events. That happens here, BEFORE
        // OnInitialized starts messaging, so nothing that arrives right after connecting is raised with no subscriber.
        return Container.Resolve<MainWindow>();
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .Build();

        // The aggregator Prism created on the UI thread. ThreadOption.UIThread relies on that: it posts to the
        // SynchronizationContext captured when the aggregator was constructed.
        IEventAggregator eventAggregator = Container.Resolve<IEventAggregator>();

        // MessagingClient = Messaging.Hosting without the .NET Generic Host, for Prism/DryIoc apps.
        MessagingClient client = MessagingClient.Create(services => services.AddMessaging(messaging => messaging
            // Broker, this app's own exchange, and the exchange it listens to ("Poc.Server"): all from appsettings.json.
            .AddBus(BusName, configuration.GetSection($"Messaging:Buses:{BusName}"))
            // Receive every [Message] class in the contracts assembly. Without this (or a handler) nothing is bound,
            // and the queue gets no messages at all.
            .AddMessages(typeof(EquipmentStatusChanged).Assembly)
            // Raise each received message on Prism's aggregator as MessageEvent<T>.
            .UseEventAggregator(eventAggregator)));

        containerRegistry.RegisterInstance(client);
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        // Returns at once: connecting happens in the background and retries every few seconds, so the window opens
        // even if RabbitMQ (or the server that owns "Poc.Server") isn't up yet.
        // Task.Run keeps the async start off the UI thread, so blocking on it here can't deadlock.
        Task.Run(() => Container.Resolve<MessagingClient>().StartAsync()).GetAwaiter().GetResult();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // ❌ DON'T call StopAsync().Wait() directly on the UI thread: closing connections awaits work that may need
        //    this thread, and the app hangs on exit. Hand it to the thread pool.
        MessagingClient client = Container.Resolve<MessagingClient>();
        Task.Run(() => client.StopAsync()).GetAwaiter().GetResult();
        client.Dispose();

        base.OnExit(e);
    }
}
