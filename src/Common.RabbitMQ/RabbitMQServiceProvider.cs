using System;
using Prism.Ioc;

namespace Common.RabbitMQ
{
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
                "No IRabbitMQService available. Set RabbitMQServiceProvider.Current, or register it in the Prism container.");
        }
    }
}
