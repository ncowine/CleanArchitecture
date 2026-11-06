using System;

namespace Messaging
{
    /// <summary>
    /// On an <see cref="IMessageHandler{TMessage}"/> class: also receive the messages a publisher sends with this routing key,
    /// as <see cref="SubscribeOptions"/> does for a runtime subscription. A handler lives as long as the application, so its
    /// binding is made at startup and stays.
    /// <code><![CDATA[
    /// [RoutingKey("equipment", Exchange = "Poc.Net472")]
    /// public sealed class EquipmentDisposedHandler : IMessageHandler<EquipmentDisposed> { ... }
    /// ]]></code>
    /// </summary>
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

        /// <summary>The exchange to bind the key on. Null means the bus's own exchange and every configured subscription.</summary>
        public string Exchange { get; set; }
    }
}
