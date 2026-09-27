using System;
using System.Threading;
using System.Threading.Tasks;
using Messaging.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;

namespace Messaging.Hosting
{
    /// <summary>
    /// Runs every handler registered for the message on this bus, then every <see cref="IMessageSink"/>, in a new DI scope
    /// per message, inside the message's correlation ID. They run one after another; the first exception fails the message.
    /// </summary>
    internal sealed class ScopedDispatcher : IInboundDispatcher
    {
        private readonly IServiceScopeFactory scopeFactory;
        private readonly MessagingRegistry registry;
        private readonly string busName;

        public ScopedDispatcher(IServiceScopeFactory scopeFactory, MessagingRegistry registry, string busName)
        {
            this.scopeFactory = scopeFactory;
            this.registry = registry;
            this.busName = busName;
        }

        public async Task Dispatch(MessageRegistration registration, object message, MessageContext context, CancellationToken cancellationToken)
        {
            // A legacy sender sets no correlation ID; the message starts a new conversation.
            using (CorrelationContext.Begin(context.CorrelationId ?? context.MessageId ?? Guid.NewGuid().ToString("N")))
            {
                AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                try
                {
                    foreach (HandlerRegistration handler in registry.GetHandlers(registration.MessageType, busName))
                    {
                        object instance = scope.ServiceProvider.GetRequiredService(handler.HandlerType);
                        await handler.Invoke(instance, message, context, cancellationToken).ConfigureAwait(false);
                    }

                    foreach (IMessageSink sink in scope.ServiceProvider.GetServices<IMessageSink>())
                    {
                        await sink.Deliver(message, context, cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    await scope.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
