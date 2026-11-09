using System;

namespace Messaging
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class RoutingKeyAttribute : Attribute
    {
        public RoutingKeyAttribute(string routingKey)
        {
            if (string.IsNullOrWhiteSpace(routingKey))
            {
                throw new ArgumentException("A routing key is required.", nameof(routingKey));
            }

            RoutingKey = routingKey;
        }

        public string RoutingKey { get; }

        public string Exchange { get; set; }
    }
}
