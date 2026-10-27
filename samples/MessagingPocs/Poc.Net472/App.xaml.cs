using System.Windows;
using Common.RabbitMQ;
using Microsoft.Extensions.Configuration;
using Poc.Legacy.Events;
using Poc.Net472.Views;
using Poc.Shared;
using Prism.DryIoc;
using Prism.Events;
using Prism.Ioc;

namespace Poc.Net472;

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
// net472 — a legacy .NET Framework app, legacy event format
//
// WHAT IT STANDS FOR
//   An existing .NET Framework WPF app built on Prism, whose events are plain PubSubEvent<T> classes. It keeps working
//   exactly as legacy apps always have: the only messaging code is the registration below (docs/messaging/README.md,
//   recipe A) and PublishRemote / Subscribe in the view model.
//
// WHO IT TALKS TO
//   Receives  the API's Equipment events          (binds the API's exchange "CleanArch")
//             other net472 windows' events         (its own exchange "Poc.Net472")
//             the net8 app's events                (binds "Poc.Net8")
//   Sends     EquipmentNoteSent  → other net472 windows and the net8 app
//             EquipmentDisposed  → the API (which deletes the equipment), other net472 windows and the net8 app
//   The net10 app never sees what this app sends: it binds only the API's exchange.
//
// ✅ DO keep the events where they are and register their assembly; no attributes or special base class needed.
// ❌ DON'T change an event's namespace or class name: the full name IS the wire name. Renaming one silently cuts off
//    every app that listens for the old name, the API included.
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
public partial class App : PrismApplication
{
    public const string BusName = "Main";

    private RabbitMQServiceRouter? _router;

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

        _router = new RabbitMQServiceRouter(Container.Resolve<IEventAggregator>(), registry);
        foreach (IConfigurationSection bus in configuration.GetSection("Messaging:Buses").GetChildren())
        {
            RabbitMQConfig config = bus.Get<RabbitMQConfig>()!;
            config.BusName = bus.Key;
            _router.AddBus(config);
        }

        containerRegistry.RegisterInstance(_router);
        containerRegistry.RegisterInstance<IRabbitMQService>(_router);   // PublishRemote finds it here

        ApiOptions api = configuration.GetSection(ApiOptions.SectionName).Get<ApiOptions>() ?? new ApiOptions();
        containerRegistry.RegisterInstance(EquipmentApi.Create(api));
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        // Returns at once: connecting happens in the background and retries every few seconds.
        _router!.Init();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _router?.Dispose();
        base.OnExit(e);
    }
}
