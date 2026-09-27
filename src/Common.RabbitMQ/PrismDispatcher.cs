using System;
using System.Threading;
using System.Threading.Tasks;
using Messaging;
using Messaging.RabbitMQ;
using Prism.Events;

namespace Common.RabbitMQ
{
    /// <summary>Raises each received message as its Prism event: <c>GetEvent&lt;T&gt;().Publish(payload)</c>, on the consumer's thread.</summary>
    internal sealed class PrismDispatcher : IInboundDispatcher
    {
        private readonly IEventAggregator eventAggregator;
        private readonly RemoteEventRegistry registry;

        public PrismDispatcher(IEventAggregator eventAggregator, RemoteEventRegistry registry)
        {
            this.eventAggregator = eventAggregator;
            this.registry = registry;
        }

        public Task Dispatch(MessageRegistration registration, object message, MessageContext context, CancellationToken cancellationToken)
        {
            // Wire names are the events' full names, so every subscribed wire name is in the registry.
            if (!registry.TryGet(registration.WireName, out RemoteEventDescriptor descriptor))
            {
                throw new InvalidOperationException($"No remote event named '{registration.WireName}'.");
            }

            descriptor.PublishLocal(eventAggregator, message);
            return Task.CompletedTask;
        }
    }
}
