using System;

namespace Messaging.Hosting
{
    public sealed class HandlerBuilder
    {
        private readonly MessagingBuilder builder;
        private readonly Type messageType;
        private readonly Type handlerType;

        internal HandlerBuilder(MessagingBuilder builder, Type messageType, Type handlerType)
        {
            this.builder = builder;
            this.messageType = messageType;
            this.handlerType = handlerType;
        }

        /// <summary>Subscribes each of <paramref name="busNames"/> to the message and sends it to this handler.</summary>
        public MessagingBuilder From(params string[] busNames)
        {
            builder.Registry.AddHandler(new HandlerRegistration(messageType, handlerType, MessagingBuilder.CheckBusNames(busNames)));
            return builder;
        }
    }
}
