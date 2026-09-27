using System;
using System.Collections.Generic;

namespace Messaging.Hosting
{
    /// <summary>A message type the application receives without a handler of its own, for example for an <see cref="IMessageSink"/>.</summary>
    internal sealed class MessageTypeRegistration
    {
        public MessageTypeRegistration(Type messageType, IReadOnlyCollection<string> buses)
        {
            MessageType = messageType;
            Buses = buses;
        }

        public Type MessageType { get; }

        /// <summary>Empty means every bus.</summary>
        public IReadOnlyCollection<string> Buses { get; }
    }
}
