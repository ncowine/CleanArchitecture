using System;
using System.Collections.Generic;

namespace Messaging.Hosting
{
    /// <summary>Where a message type is published, and with which routing key.</summary>
    internal sealed class RouteRegistration
    {
        public RouteRegistration(Type messageType)
        {
            MessageType = messageType;
        }

        public Type MessageType { get; }

        /// <summary>Empty means the application's only bus.</summary>
        public List<string> Buses { get; } = new List<string>();

        /// <summary>Null means the wire name.</summary>
        public Func<object, string> RoutingKey { get; set; }
    }
}
