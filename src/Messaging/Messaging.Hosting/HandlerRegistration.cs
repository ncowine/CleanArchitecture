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

        public IReadOnlyCollection<string> Buses { get; }

        public Func<object, object, MessageContext, CancellationToken, Task> Invoke { get; }
    }
}
