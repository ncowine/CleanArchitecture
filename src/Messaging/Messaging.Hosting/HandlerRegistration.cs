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

        /// <summary>The buses this handler receives from. Empty means every bus.</summary>
        public IReadOnlyCollection<string> Buses { get; }

        /// <summary>
        /// (handler, message, context, token) → handler.Handle(message, context, token). Built where both types are known,
        /// so dispatching a message needs no reflection.
        /// </summary>
        public Func<object, object, MessageContext, CancellationToken, Task> Invoke { get; }
    }
}
