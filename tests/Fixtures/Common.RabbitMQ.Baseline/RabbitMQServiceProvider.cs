using System;
using Prism.Ioc;

namespace Common.RabbitMQ
{
    /// <summary>
    /// Service locator used by <see cref="RemotePubSubEventExtensions.PublishRemote{TPayload}"/> when no service is passed in.
    /// Uses <see cref="Current"/> when set (e.g. in the API), otherwise Prism's <see cref="ContainerLocator"/>.
    /// </summary>
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
