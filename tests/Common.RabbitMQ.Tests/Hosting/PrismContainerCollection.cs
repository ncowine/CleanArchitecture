using Xunit;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>Tests that set Prism's static <c>ContainerLocator</c> must not run at the same time.</summary>
    [CollectionDefinition(Name)]
    public sealed class PrismContainerCollection
    {
        public const string Name = "PrismContainer";
    }
}
