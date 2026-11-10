using System.Linq;
using System.Threading.Tasks;
using Messaging;
using Messaging.Hosting;
using Xunit;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>A handler registered by several AddHandlers calls binds its [RoutingKey] on all of their buses.</summary>
    public class HandlerRoutingKeyBusesTests
    {
        [Fact]
        public void TheBusesOfEveryRegistration_AreCombined()
        {
            MessagingRegistry registry = new MessagingRegistry();
            registry.AddHandler(Registration("A"));
            registry.AddHandler(Registration("B", "A"));

            (RoutingKeyAttribute key, System.Collections.Generic.IReadOnlyCollection<string> buses) = Assert.Single(registry.GetHandlerRoutingKeys());

            Assert.Equal(KeyedMessageHandler.Key, key.RoutingKey);
            Assert.Equal(new[] { "A", "B" }, buses.OrderBy(b => b));
        }

        [Fact]
        public void ARegistrationOnEveryBus_MeansEveryBus()
        {
            MessagingRegistry registry = new MessagingRegistry();
            registry.AddHandler(Registration("A"));
            registry.AddHandler(Registration());

            Assert.Empty(Assert.Single(registry.GetHandlerRoutingKeys()).Buses);
        }

        private static HandlerRegistration Registration(params string[] buses)
        {
            return new HandlerRegistration(typeof(KeyedMessage), typeof(KeyedMessageHandler), buses, (handler, message, context, token) => Task.CompletedTask);
        }
    }
}
