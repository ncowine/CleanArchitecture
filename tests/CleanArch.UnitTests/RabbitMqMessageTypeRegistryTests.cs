using Common.RabbitMQ;
using Xunit;

namespace CleanArch.UnitTests;

public class RabbitMqMessageTypeRegistryTests
{
    private sealed record TestMessageA(string Value);

    private sealed record TestMessageB(string Value);

    [Fact]
    public void Registered_type_resolves_both_directions()
    {
        var registry = new MessageTypeRegistry();

        registry.Register<TestMessageA>("TestMessageA");

        Assert.Equal("TestMessageA", registry.GetMessageType<TestMessageA>());
        Assert.Equal("TestMessageA", registry.GetMessageType(typeof(TestMessageA)));
        Assert.Equal(typeof(TestMessageA), registry.ResolveClrType("TestMessageA"));
    }

    [Fact]
    public void Unknown_wire_name_resolves_to_null_not_a_thrown_type_lookup()
    {
        var registry = new MessageTypeRegistry();

        Assert.Null(registry.ResolveClrType("NeverRegistered"));
    }

    [Fact]
    public void Unregistered_type_throws_on_publish_lookup()
    {
        var registry = new MessageTypeRegistry();

        Assert.Throws<InvalidOperationException>(() => registry.GetMessageType<TestMessageA>());
    }

    [Fact]
    public void Registering_the_same_wire_name_to_a_different_type_throws()
    {
        var registry = new MessageTypeRegistry();
        registry.Register<TestMessageA>("Shared");

        Assert.Throws<InvalidOperationException>(() => registry.Register<TestMessageB>("Shared"));
    }

    [Fact]
    public void Registering_the_same_type_to_a_different_wire_name_throws()
    {
        var registry = new MessageTypeRegistry();
        registry.Register<TestMessageA>("NameOne");

        Assert.Throws<InvalidOperationException>(() => registry.Register<TestMessageA>("NameTwo"));
    }

    [Fact]
    public void Registering_the_same_pair_twice_is_a_harmless_no_op()
    {
        var registry = new MessageTypeRegistry();
        registry.Register<TestMessageA>("TestMessageA");

        registry.Register<TestMessageA>("TestMessageA");

        Assert.Equal("TestMessageA", registry.GetMessageType<TestMessageA>());
    }
}
