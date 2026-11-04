using Xunit;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary>Tests that set the static <see cref="RabbitMQServiceProvider.Current"/> must not run at the same time.</summary>
    [CollectionDefinition(Name)]
    public sealed class ServiceProviderCollection
    {
        public const string Name = "RabbitMQServiceProvider";
    }
}
