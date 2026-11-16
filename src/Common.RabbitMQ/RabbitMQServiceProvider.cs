using System;
using Prism.Ioc;

namespace Common.RabbitMQ
{
    internal static class RabbitMQServiceProvider
    {
        public static IRabbitMQService Current { get; set; }

        public static IRabbitMQService Resolve()
        {
            if (Current == null)
            {
                IContainerProvider container = ContainerLocator.Container
                    ?? throw new InvalidOperationException("No Prism container found.");

                if (!container.IsRegistered<IRabbitMQService>())
                {
                    throw new InvalidOperationException("No IRabbitMQService registered in the Prism container.");
                }

                Current = container.Resolve<IRabbitMQService>();
            }

            return Current;
        }
    }
}
