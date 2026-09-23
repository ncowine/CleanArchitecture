using System.Text.Json;
using Common.RabbitMQ;
using Xunit;

namespace CleanArch.UnitTests;

public class RabbitMqEnvelopeSerializerTests
{
    private sealed record TestMessage(string Name, int Count);

    [Fact]
    public void Wire_bytes_carry_the_registered_message_type_and_the_payload()
    {
        var binding = new PublishBinding("equipment", RabbitExchangeType.Topic, "equipment.reserved");
        var message = new TestMessage("laptop", 3);

        var raw = EnvelopeSerializer.ToRawMessage(message, "TestMessage", binding, correlationId: "corr-1");

        using var document = JsonDocument.Parse(raw.Body);
        var root = document.RootElement;

        Assert.Equal("TestMessage", root.GetProperty("messageType").GetString());
        Assert.Equal("corr-1", root.GetProperty("correlationId").GetString());
        Assert.NotEqual(Guid.Empty, root.GetProperty("messageId").GetGuid());
        Assert.Equal("laptop", root.GetProperty("payload").GetProperty("name").GetString());
        Assert.Equal(3, root.GetProperty("payload").GetProperty("count").GetInt32());
    }

    [Fact]
    public void Raw_message_carries_the_binding_exactly_as_given()
    {
        var binding = new PublishBinding("equipment", RabbitExchangeType.Topic, "equipment.reserved");

        var raw = EnvelopeSerializer.ToRawMessage(new TestMessage("x", 1), "TestMessage", binding, correlationId: null);

        Assert.Equal(binding.Exchange, raw.Exchange);
        Assert.Equal(binding.ExchangeType, raw.ExchangeType);
        Assert.Equal(binding.RoutingKey, raw.RoutingKey);
        Assert.Null(raw.CorrelationId);
    }

    [Fact]
    public void Every_call_gets_its_own_message_id()
    {
        var binding = new PublishBinding("equipment", RabbitExchangeType.Topic, "equipment.reserved");
        var message = new TestMessage("x", 1);

        var first = EnvelopeSerializer.ToRawMessage(message, "TestMessage", binding, correlationId: null);
        var second = EnvelopeSerializer.ToRawMessage(message, "TestMessage", binding, correlationId: null);

        using var firstDocument = JsonDocument.Parse(first.Body);
        using var secondDocument = JsonDocument.Parse(second.Body);

        Assert.NotEqual(
            firstDocument.RootElement.GetProperty("messageId").GetGuid(),
            secondDocument.RootElement.GetProperty("messageId").GetGuid());
    }
}
