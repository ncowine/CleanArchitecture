using System;
using System.Threading;
using System.Threading.Tasks;

namespace Messaging.Hosting
{
    public sealed class HandlerBuilder
    {
        private readonly MessagingBuilder builder;
        private readonly Type messageType;
        private readonly Type handlerType;
        private readonly Func<object, object, MessageContext, CancellationToken, Task> invoke;

        internal HandlerBuilder(MessagingBuilder builder, Type messageType, Type handlerType, Func<object, object, MessageContext, CancellationToken, Task> invoke)
        {
            this.builder = builder;
            this.messageType = messageType;
            this.handlerType = handlerType;
            this.invoke = invoke;
        }

        /// <summary>Subscribes each of <paramref name="busNames"/> to the message and sends it to this handler.</summary>
        public MessagingBuilder From(params string[] busNames)
        {
            builder.Registry.AddHandler(new HandlerRegistration(messageType, handlerType, MessagingBuilder.CheckBusNames(busNames), invoke));
            return builder;
        }
    }
}
