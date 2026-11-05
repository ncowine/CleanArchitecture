using System;
using System.Threading;
using System.Threading.Tasks;

namespace Messaging.Hosting
{
    internal sealed class MessageSubscription
    {
        public MessageSubscription(Type messageType, Func<object, MessageContext, CancellationToken, Task> invoke)
        {
            MessageType = messageType;
            Invoke = invoke;
        }

        public Type MessageType { get; }

        public Func<object, MessageContext, CancellationToken, Task> Invoke { get; }
    }
}
