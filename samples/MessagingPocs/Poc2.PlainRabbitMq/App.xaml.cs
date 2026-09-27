using System.Windows;
using Messaging.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Equipment.Messages;
using Poc.Shared;
using Poc2.PlainRabbitMq.ViewModels;
using Poc2.PlainRabbitMq.Views;

namespace Poc2.PlainRabbitMq;

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
// POC 2 — RabbitMQ without IEventAggregator
//
// WHEN THIS IS THE RIGHT CHOICE
//   A .NET 8 app that must get events straight from the broker — for example events that legacy apps publish and the
//   API never sees — and doesn't need Prism. Fewer moving parts than POC 1: no event aggregator in between, no weak
//   references, and the same Generic Host setup as the API.
//   If the events all come from the API, prefer POC 3 (SignalR): no broker account or broker access on each desktop.
//
// HOW IT'S WIRED
//   The API publishes Equipment's events (Equipment.Messages) to its exchange "CleanArch" through its outbox; this
//   app binds a queue to it. The .NET Generic Host gives DI, appsettings.json and logging, and runs messaging as a
//   hosted service. View models inject IMessageSubscriber and subscribe to message classes directly. Loading and
//   changes go to the API over HTTP (Poc.Shared.EquipmentApi).
//
// ❌ DON'T publish from here (no Route<T>(), no IMessagePublisher). Modern apps only listen; changes go to the API.
// ❌ DON'T put broker credentials that can publish or configure the server's exchange in a desktop app's config. Give
//    desktop apps their own restricted RabbitMQ account.
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
public partial class App : Application
{
    public const string BusName = "Main";

    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = e.Args,
            // ✅ DO set the content root to the exe's folder. The default is the current directory, so starting the
            //    app from a shortcut or another folder wouldn't find appsettings.json.
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Services.AddMessaging(messaging => messaging
            .AddBus(BusName, builder.Configuration.GetSection($"Messaging:Buses:{BusName}"))
            // ✅ DO register what you receive. IMessageSubscriber only sees message types the bus is bound for:
            //    AddMessages(assembly) receives every [Message] class in it. Subscribing to a type that isn't
            //    registered compiles and runs, and simply never fires.
            .AddMessages(typeof(EquipmentCreated).Assembly));

        ApiOptions api = builder.Configuration.GetSection(ApiOptions.SectionName).Get<ApiOptions>() ?? new ApiOptions();
        builder.Services.AddSingleton(EquipmentApi.Create(api));

        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        _host = builder.Build();

        // ✅ DO build the window, and so its view model's subscriptions, BEFORE starting the host. StartAsync begins
        //    connecting; a message that arrives before anyone has subscribed is received and dropped.
        //    Building it here, on the UI thread, also gives the view model the UI's SynchronizationContext.
        MainWindow window = _host.Services.GetRequiredService<MainWindow>();
        await _host.StartAsync();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            // ❌ DON'T block on StopAsync() directly on the UI thread: it can deadlock. Hand it to the thread pool.
            Task.Run(() => _host.StopAsync()).GetAwaiter().GetResult();

            // Disposes the singletons too, including the view model and its subscriptions.
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
