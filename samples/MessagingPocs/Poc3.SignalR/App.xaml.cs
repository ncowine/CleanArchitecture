using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Poc.Shared;
using Poc3.SignalR.ViewModels;
using Poc3.SignalR.Views;

namespace Poc3.SignalR;

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
// POC 3 — SignalR from the API
//
// WHEN THIS IS THE RIGHT CHOICE
//   The default for new modern desktop apps whose events come from the API (docs/messaging/adr/0003: the API makes
//   the changes and modern apps only listen). The app talks to one thing, the API: HTTP for reads and changes,
//   SignalR for "something changed". No broker account on the desktop, no broker port open to it, no messaging
//   library to keep in step with the legacy apps.
//   Not the right choice when the app needs events that only legacy apps publish: the API would have to receive and
//   forward them first (POC 2 receives them directly).
//
// HOW IT'S WIRED
//   The API pushes EquipmentCreated / Updated / Deleted (the Equipment.Messages classes) to the "equipment" group on
//   its hub right after each change commits (tutorials/65-real-time-notifications.md). This app joins that group.
//   The same change also goes to RabbitMQ through the outbox for POC 1 and 2: one feature, two channels.
//
// WHAT SIGNALR IS NOT
//   ❌ Not a queue. If the app isn't connected when an event is sent, that event is gone. There's no replay.
//      So the app loads current state over HTTP on start and after every reconnect, and uses events only to stay
//      current in between. That's the same rule as for the RabbitMQ POCs: desktop queues also vanish when the app
//      closes.
//   ❌ Not a way to change data. Changes go to the API over HTTP.
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = e.Args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        ApiOptions api = builder.Configuration.GetSection(ApiOptions.SectionName).Get<ApiOptions>() ?? new ApiOptions();
        builder.Services.AddSingleton(api);

        builder.Services.AddSingleton(EquipmentApi.Create(api));   // one HttpClient for the app's life (see EquipmentApi)

        builder.Services.AddSingleton<EquipmentLiveUpdates>();
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        _host = builder.Build();

        MainWindow window = _host.Services.GetRequiredService<MainWindow>();
        window.Show();

        // Show the window first, then connect: connecting retries until the API is up, and the window says so.
        await _host.Services.GetRequiredService<MainWindowViewModel>().StartAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            // ✅ DO close the hub connection on exit, so the API sees the client leave (presence updates at once
            //    rather than after a timeout). Off the UI thread, so it can't deadlock.
            EquipmentLiveUpdates live = _host.Services.GetRequiredService<EquipmentLiveUpdates>();
            Task.Run(async () => await live.DisposeAsync()).GetAwaiter().GetResult();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
