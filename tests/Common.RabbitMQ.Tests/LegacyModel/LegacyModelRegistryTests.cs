using System.Linq;
using AppA.Events;
using Shared.Events;
using Xunit;
using LegacyConfig = Legacy.RabbitMQ.RabbitMQConfig;
using LegacyDescriptor = Legacy.RabbitMQ.RemoteEventDescriptor;
using LegacyRegistry = Legacy.RabbitMQ.RemoteEventRegistry;

namespace Common.RabbitMQ.Tests.LegacyModel
{
    /// <summary>Legacy model facts and assumptions that need no broker. IDs refer to docs/legacy-baseline-assumptions.md.</summary>
    public class LegacyModelRegistryTests
    {
        [Fact]
        public void F1_PlainPubSubEvents_AreFound_WithoutAttributesOrBaseClass()
        {
            LegacyRegistry registry = new LegacyRegistry(typeof(OrderSaved).Assembly);

            Assert.True(registry.TryGet(typeof(OrderSaved), out LegacyDescriptor descriptor));
            Assert.Equal(typeof(Order), descriptor.PayloadType);
            Assert.Equal("AppA.Events.OrderSaved", descriptor.EventName);
        }

        [Fact]
        public void F1_LocalOnlyEvents_AreRegisteredToo()
        {
            // Nothing marks an event as local, so it is registered; it only travels if someone calls PublishRemote on it.
            LegacyRegistry registry = new LegacyRegistry(typeof(OrderSaved).Assembly);

            Assert.Equal(new[] { "AppA.Events.OrderSaved", "AppA.Events.OrderSelected" }, registry.Events.Select(e => e.EventName).OrderBy(n => n));
        }

        [Fact]
        public void F1_SharedNetStandardEvents_AreFound()
        {
            LegacyRegistry registry = new LegacyRegistry(typeof(OrderSaved).Assembly, typeof(UserChanged).Assembly);

            Assert.True(registry.TryGet("Shared.Events.UserChanged", out LegacyDescriptor descriptor));
            Assert.Equal(typeof(User), descriptor.PayloadType);
        }

        [Fact]
        public void F3_VirtualHost_IsTheDefault()
        {
            Assert.Equal("/", new LegacyConfig().VirtualHost);
        }
    }
}
