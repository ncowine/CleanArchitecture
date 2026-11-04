using System;
using Messaging;
using Prism.Ioc;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>Makes <paramref name="publisher"/> what <c>MessageEventExtensions</c> finds in Prism's container, until disposed.</summary>
    public sealed class PublisherInContainer : IDisposable
    {
        public PublisherInContainer(IMessagePublisher publisher)
        {
            ContainerLocator.ResetContainer();
            ContainerLocator.SetContainerExtension(() => PublisherOnlyContainer.For(publisher));
        }

        public void Dispose()
        {
            ContainerLocator.ResetContainer();
        }
    }
}
