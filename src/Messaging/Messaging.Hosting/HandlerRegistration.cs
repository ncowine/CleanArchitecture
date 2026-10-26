using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Messaging.Hosting
{
    internal sealed class HandlerRegistration
    {
        public HandlerRegistration(Type messageType, Type handlerType, IReadOnlyCollection<string> buses, Func<object, object, MessageContext, CancellationToken, Task> invoke)
        {
            MessageType = messageType;
            HandlerType = handlerType;
            Buses = buses;
            Invoke = invoke;
        }

        public Type MessageType { get; }

        public Type HandlerType { get; }

        /// <summary>The buses this handler receives from.</summary>
        public IReadOnlyCollection<string> Buses { get; }

        /// <summary>
        /// (handler, message, context, token) → handler.Handle(message, context, token). Built in
        /// <see cref="MessagingBuilder.Handle{TMessage, THandler}"/>, where both types are known, so no reflection is needed.
        /// </summary>
        public Func<object, object, MessageContext, CancellationToken, Task> Invoke { get; }
    }
}
