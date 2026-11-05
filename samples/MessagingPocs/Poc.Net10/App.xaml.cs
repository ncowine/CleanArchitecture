using System.Windows;
using Equipment.Messages;
using Messaging.Hosting;
using Messaging.RabbitMQ;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Poc.Net10.ViewModels;
using Poc.Net10.Views;
using Poc.Shared;

namespace Poc.Net10;

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
// net10 — a modern app, modern event format, API events (+ the POC shape events)
//
// WHAT IT STANDS FOR
//   A new .NET 10 desktop app. It hears about changes from the API, in the modern format: plain [Message] classes
//   (Equipment.Messages, the API's own contracts). No Prism, no legacy adapter, no legacy events.
//   For the POC it also receives the ten shape events the legacy apps send (Poc.Shapes), to show the legacy format
//   read as plain classes.
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
// ❌ DON'T publish from here (no IMessagePublisher). Modern apps only listen; changes go to the API.
// ❌ DON'T bind a legacy app's exchange without routing keys "just to see it": every event it receives would bind there.
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
public partial class App : Application
{
    public const string BusName = "Main";

    private IHost? host;

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

        BusLog busLog = new BusLog();

        if (useSignalR)
        {
            builder.Services.AddSingleton<EquipmentLiveUpdates>();
        }
        else
        {
            // The bus logs through ILogger like everything else here; this provider also shows it in the window.
            builder.Logging.AddProvider(busLog);

            builder.Services.AddMessaging(messaging => messaging
                // Subscribing in the view model is all it takes: what nothing subscribes to is dropped on arrival.
                .AddBus(BusName, builder.Configuration.GetSection($"Messaging:Buses:{BusName}"))
                .UseDefaultBus(builder.Configuration["Messaging:DefaultBus"]));
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
            viewModel.ListenOverRabbitMq(
                host.Services.GetRequiredService<IMessageSubscriber>(),
                host.Services.GetRequiredKeyedService<RabbitMQBus>(BusName),
                busLog);
            await host.StartAsync();   // starts connecting in the background
            window.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
