using System;
using System.Threading;
using System.Threading.Tasks;
using Messaging;
using Messaging.RabbitMQ;
using Prism.Events;

namespace Common.RabbitMQ
{
    internal sealed class PrismDispatcher : IInboundDispatcher
    {
        private readonly IEventAggregator eventAggregator;
        private readonly RemoteEventRegistry registry;

        public PrismDispatcher(IEventAggregator eventAggregator, RemoteEventRegistry registry)
        {
            this.eventAggregator = eventAggregator;
            this.registry = registry;
        }

        public Task DispatchAsync(MessageRegistration registration, object message, MessageContext context, CancellationToken cancellationToken)
        {
            if (!registry.TryGet(registration.WireName, out RemoteEventDescriptor descriptor))
            {
                throw new InvalidOperationException($"No remote event named '{registration.WireName}'.");
            }

            descriptor.PublishLocal(eventAggregator, message);
            return Task.CompletedTask;
        }
    }
}
