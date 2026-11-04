using System;
using System.Reflection;
using Messaging;
using Prism.Ioc;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>A Prism container that only knows one <see cref="IMessagePublisher"/>. Every other member does nothing.</summary>
    public class PublisherOnlyContainer : DispatchProxy
    {
        private IMessagePublisher publisher;

        public static IContainerExtension For(IMessagePublisher publisher)
        {
            IContainerExtension container = Create<IContainerExtension, PublisherOnlyContainer>();
            ((PublisherOnlyContainer)(object)container).publisher = publisher;
            return container;
        }

        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            return targetMethod.Name == nameof(IContainerProvider.Resolve) && (Type)args[0] == typeof(IMessagePublisher)
                ? publisher
                : null;
        }
    }
}
