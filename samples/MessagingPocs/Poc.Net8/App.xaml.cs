using System.Windows;
using Common.RabbitMQ;
using Microsoft.Extensions.Configuration;
using Poc.Legacy.Events;
using Poc.Net8.Views;
using Poc.Shared;
using Prism.DryIoc;
using Prism.Events;
using Prism.Ioc;

namespace Poc.Net8;

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
// net8 — a legacy app moved to .NET 8, still in the legacy event format
//
// WHAT IT STANDS FOR
//   A legacy Prism app part-way through a migration: it runs on .NET 8 but keeps its PubSubEvent<T> events and the
//   legacy adapter, so it still talks to the .NET Framework apps exactly as before. The registration below is the
//   net472 app's, line for line; the adapter targets both frameworks.
//
// WHO IT TALKS TO
//   Receives  the API's Equipment events          (binds the API's exchange "CleanArch")
//             other net8 windows' events           (its own exchange "Poc.Net8")
//             the net472 app's events              (binds "Poc.Net472": notes, and disposals it sends the API)
//   Sends     EquipmentNoteSent  → other net8 windows and the net472 app
//   It sends nothing to the API (the API binds only "Poc.Net472"), and the net10 app never sees what it sends.
//
// ✅ DO keep the events where they are and register their assembly; no attributes or special base class needed.
// ❌ DON'T change an event's namespace or class name: the full name IS the wire name. Renaming one silently cuts off
//    every app that listens for the old name, the API included.
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
public partial class App : PrismApplication
{
    public const string BusName = "Main";

    private RabbitMQServiceRouter? router;

    protected override Window CreateShell()
    {
        // Resolving the window builds its view model, which subscribes to the Prism events. That happens here, BEFORE
        // OnInitialized starts messaging, so nothing that arrives right after connecting is raised with no subscriber.
        return Container.Resolve<MainWindow>();
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .Build();

        // Every PubSubEvent<T> in the assembly can travel between apps, known by its full type name. That includes
        // FromApi/, whose classes are named after the API's events, so the API's messages arrive as Prism events too.
        RemoteEventRegistry registry = new RemoteEventRegistry()
            .Add(typeof(EquipmentNoteSent).Assembly);

        router = new RabbitMQServiceRouter(Container.Resolve<IEventAggregator>(), registry);
        foreach (IConfigurationSection bus in configuration.GetSection("Messaging:Buses").GetChildren())
        {
            RabbitMQConfig config = bus.Get<RabbitMQConfig>()!;
            config.BusName = bus.Key;
            router.AddBus(config);
        }

        containerRegistry.RegisterInstance(router);
        containerRegistry.RegisterInstance<IRabbitMQService>(router);   // PublishRemote finds it here

        ApiOptions api = configuration.GetSection(ApiOptions.SectionName).Get<ApiOptions>() ?? new ApiOptions();
        containerRegistry.RegisterInstance(EquipmentApi.Create(api));
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        // Returns at once: connecting happens in the background and retries every few seconds.
        router!.Init();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        router?.Dispose();
        base.OnExit(e);
    }
}
