using System;
using System.Threading;
using System.Threading.Tasks;
using Messaging.RabbitMQ;

namespace Messaging.Hosting
{
    internal sealed class MessageSubscription
    {
        public MessageSubscription(Type messageType, SubscribeOptions options, RabbitMQBus bindingBus, Func<object, MessageContext, CancellationToken, Task> invoke)
        {
            MessageType = messageType;
            Options = options;
            BindingBus = bindingBus;
            Invoke = invoke;
        }

        public Type MessageType { get; }

        public SubscribeOptions Options { get; }

        public RabbitMQBus BindingBus { get; }

        public Func<object, MessageContext, CancellationToken, Task> Invoke { get; }
    }
}
