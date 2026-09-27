using System;
using System.Threading;
using System.Threading.Tasks;

namespace Messaging.Hosting
{
    /// <summary>One subscription made through <see cref="IMessageSubscriber"/>.</summary>
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
