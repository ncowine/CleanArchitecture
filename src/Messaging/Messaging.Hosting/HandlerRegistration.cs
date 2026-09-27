using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Messaging.Hosting
{
    internal sealed class HandlerRegistration
    {
        public HandlerRegistration(Type messageType, Type handlerType, IReadOnlyCollection<string> buses)
        {
            MessageType = messageType;
            HandlerType = handlerType;
            Buses = buses;

            MethodInfo invoke = typeof(HandlerInvoker<>).MakeGenericType(messageType).GetMethod(nameof(HandlerInvoker<object>.Invoke));
            Invoke = (Func<object, object, MessageContext, CancellationToken, Task>)invoke.CreateDelegate(
                typeof(Func<object, object, MessageContext, CancellationToken, Task>));
        }

        public Type MessageType { get; }

        public Type HandlerType { get; }

        /// <summary>The buses this handler receives from.</summary>
        public IReadOnlyCollection<string> Buses { get; }

        /// <summary>(handler, message, context, token) → handler.Handle(message, context, token).</summary>
        public Func<object, object, MessageContext, CancellationToken, Task> Invoke { get; }
    }
}
