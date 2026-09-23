using Common.RabbitMQ;
using Xunit;

namespace CleanArch.UnitTests;

public class RabbitMqTopologyRegistryTests
{
    private sealed record TestMessage(string Value);

    [Fact]
    public void Mapped_binding_round_trips()
    {
        var topology = new TopologyRegistry();
        var binding = new PublishBinding("equipment", RabbitExchangeType.Topic, "equipment.reserved");

        topology.MapPublish<TestMessage>(binding);

        Assert.Equal(binding, topology.GetPublishBinding<TestMessage>());
        Assert.Equal(binding, topology.GetPublishBinding(typeof(TestMessage)));
    }

    [Fact]
    public void Unmapped_type_throws_rather_than_guessing_a_binding()
    {
        var topology = new TopologyRegistry();

        Assert.Throws<InvalidOperationException>(() => topology.GetPublishBinding<TestMessage>());
    }

    [Fact]
    public void Remapping_a_type_replaces_its_binding()
    {
        var topology = new TopologyRegistry();
        topology.MapPublish<TestMessage>(new PublishBinding("first", RabbitExchangeType.Direct, "rk"));
        var replacement = new PublishBinding("second", RabbitExchangeType.Fanout, string.Empty);

        topology.MapPublish<TestMessage>(replacement);

        Assert.Equal(replacement, topology.GetPublishBinding<TestMessage>());
    }
}
