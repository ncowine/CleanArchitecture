using System;
using Prism.Ioc;

namespace Legacy.RabbitMQ
{
    /// <summary>Resolves the service for <see cref="PubSubEventExtensions.PublishRemote{TPayload}"/> when none is passed.</summary>
    public static class RabbitMQServiceProvider
    {
        public static IRabbitMQService Current { get; set; }

        public static IRabbitMQService Resolve()
        {
            if (Current != null)
            {
                return Current;
            }

            IContainerProvider container = ContainerLocator.Container;
            if (container != null)
            {
                return container.Resolve<IRabbitMQService>();
            }

            throw new InvalidOperationException(
                "No IRabbitMQService available. Set RabbitMQServiceProvider.Current, register it in the Prism container, or pass it to PublishRemote.");
        }
    }
}
