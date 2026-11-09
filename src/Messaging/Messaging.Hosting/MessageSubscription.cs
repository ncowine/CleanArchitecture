using System;
using System.Threading;
using System.Threading.Tasks;

namespace Messaging.Hosting
{
    internal sealed class MessageSubscription
    {
        public MessageSubscription(Type messageType, SubscribeOptions options, Func<object, MessageContext, CancellationToken, Task> invoke)
        {
            MessageType = messageType;
            Options = options;
            Invoke = invoke;
        }

        public Type MessageType { get; }

        public SubscribeOptions Options { get; }

        public Func<object, MessageContext, CancellationToken, Task> Invoke { get; }
    }
}
