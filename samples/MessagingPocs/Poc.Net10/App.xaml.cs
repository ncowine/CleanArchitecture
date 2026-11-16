using System.Windows;
using Common.RabbitMQ;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Poc.Net10.ViewModels;
using Poc.Net10.Views;
using Poc.Shared;
using Prism.Events;

namespace Poc.Net10;

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
// net10 — a new .NET 10 app, API events (+ the POC shape events)
//
// WHAT IT STANDS FOR
//   A new .NET 10 desktop app on the Generic Host. It hears about changes from the API.
//   Over RabbitMQ it uses the legacy adapter (Common.RabbitMQ) and the same RemotePubSubEvent<T> classes as the
//   net472 and net8 apps (Poc.Legacy.Events): desktop apps stay on the adapter until RabbitMQ leaves the desktop, and
//   Messaging.Hosting is for the API only. Prism's event aggregator is used on its own, without PrismApplication.
//   For the POC it also receives the ten shape events the legacy apps send.
//
// HOW THE LEGACY APPS' OTHER EVENTS ARE KEPT OUT — by configuration, not by filtering in code
//   RabbitMQ: its queue binds the legacy apps' exchanges with the routing-key pattern "Poc.Legacy.Events.Shapes.#"
//             only, so the broker never routes a note or a disposal to it; and it couldn't read one anyway.
//   SignalR:  the hub is the API's, and only the API pushes to it. The shape events don't arrive in this mode.
//
// TWO TRANSPORTS, ONE APP (Events:Transport in appsettings.json)
//   RabbitMQ  right after the API's commit, best effort. Needs a broker account on the desktop.
//   SignalR   the API's hub: at once, best effort. Needs the API only — the default choice for new apps (POC 3).
//   The view model, the list and the event classes are the same either way; only the wiring below differs.
//
// ❌ DON'T publish from here (no PublishRemote). This app only listens; changes go to the API.
// ❌ DON'T bind a legacy app's exchange without routing keys "just to see it": every event it receives would bind there.
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
public partial class App : Application
{
    public const string BusName = "Main";

    private IHost? host;
    private RabbitMQServiceRouter? router;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = e.Args,
            // ✅ DO set the content root to the exe's folder, so appsettings.json is found however the app is started.
            ContentRootPath = AppContext.BaseDirectory,
        });

        bool useSignalR = string.Equals(builder.Configuration["Events:Transport"], "SignalR", StringComparison.OrdinalIgnoreCase);

        ApiOptions api = builder.Configuration.GetSection(ApiOptions.SectionName).Get<ApiOptions>() ?? new ApiOptions();
        builder.Services.AddSingleton(api);
        builder.Services.AddSingleton(EquipmentApi.Create(api));

        if (useSignalR)
        {
            builder.Services.AddSingleton<EquipmentLiveUpdates>();
        }

        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        host = builder.Build();

        // ✅ DO build the window (and so the view model, on the UI thread) and subscribe BEFORE connecting.
        MainWindow window = host.Services.GetRequiredService<MainWindow>();
        MainWindowViewModel viewModel = host.Services.GetRequiredService<MainWindowViewModel>();

        if (useSignalR)
        {
            window.Show();

            // Connecting retries until the API is up, and the window says so.
            await viewModel.ListenOverSignalRAsync(host.Services.GetRequiredService<EquipmentLiveUpdates>());
        }
        else
        {
            // ✅ DO create the event aggregator on the UI thread: ThreadOption.UIThread posts to the thread it was made on.
            IEventAggregator events = new EventAggregator();

            // The bus logs through this ILogger; it also shows the messages in the window.
            BusLog busLog = new BusLog();

            // Messaging:Buses and Messaging:DefaultBus. Subscribing to a RemotePubSubEvent<T> is all it takes: what
            // nothing subscribes to is dropped on arrival.
            router = RabbitMQServiceRouter.FromConfiguration(events, builder.Configuration, busLog);

            viewModel.ListenOverRabbitMq(events, router.GetRequiredBus(BusName), busLog);
            router.Init();   // returns at once: connecting happens in the background and retries
            window.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        router?.Dispose();

        if (host is not null)
        {
            // ❌ DON'T block on StopAsync() on the UI thread: it can deadlock. Hand it to the thread pool.
            // Disposing the host also closes the hub connection (so the API's presence updates at once) and disposes
            // the view model's subscriptions.
            IHost host = this.host;
            Task.Run(async () =>
            {
                await host.StopAsync();
                if (host is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else
                {
                    host.Dispose();
                }
            }).GetAwaiter().GetResult();
        }

        base.OnExit(e);
    }
}
