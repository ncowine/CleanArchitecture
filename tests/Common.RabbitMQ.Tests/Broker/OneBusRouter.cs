using Microsoft.Extensions.Logging.Abstractions;
using Prism.Events;

namespace Common.RabbitMQ.Tests.Broker
{
    /// <summary>Starts a router with one bus, which is also the default: how an application with one bus runs.</summary>
    internal static class OneBusRouter
    {
        public static RabbitMQServiceRouter Start(IEventAggregator eventAggregator, RabbitMQConfig config)
        {
            RabbitMQServiceRouter router = new RabbitMQServiceRouter(eventAggregator, NullLogger.Instance) { DefaultBus = config.BusName };
            router.AddBus(config);
            router.Init();
            return router;
        }
    }
}
