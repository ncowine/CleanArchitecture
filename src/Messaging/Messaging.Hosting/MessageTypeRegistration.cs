using System;
using System.Collections.Generic;

namespace Messaging.Hosting
{
    internal sealed class MessageTypeRegistration
    {
        public MessageTypeRegistration(Type messageType, IReadOnlyCollection<string> buses)
        {
            MessageType = messageType;
            Buses = buses;
        }

        public Type MessageType { get; }

        public IReadOnlyCollection<string> Buses { get; }
    }
}
