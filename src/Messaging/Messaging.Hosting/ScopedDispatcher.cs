using System.Threading;
using System.Threading.Tasks;
using Messaging.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;

namespace Messaging.Hosting
{
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
