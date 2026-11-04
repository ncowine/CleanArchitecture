using System;
using System.Reflection;
using Common.RabbitMQ.Tests.Broker;
using Prism.Events;

namespace Common.RabbitMQ.Tests.LegacyModel
{
    /// <summary>
    /// An application on the current <c>Common.RabbitMQ</c> adapter in the ADR 0002 topology: events registered by
    /// assembly with <see cref="RemoteEventRegistry.Add"/>, its own exchange, subscriptions and routing keys from
    /// <see cref="RabbitMQConfig"/>. Inside this namespace the unqualified names bind to the current adapter.
    /// </summary>
    public sealed class AdapterApp : IDisposable
    {
        private readonly EventAggregator eventAggregator = new EventAggregator();
        private readonly RabbitMQService service;

        public AdapterApp(BrokerSettings broker, string exchangeName, string clientName, Action<RabbitMQConfig> configure, Assembly[] eventAssemblies)
        {
            RemoteEventRegistry registry = new RemoteEventRegistry();
            foreach (Assembly assembly in eventAssemblies)
            {
                registry.Add(assembly);
            }

            RabbitMQConfig config = new RabbitMQConfig
            {
                BusName = clientName,
                HostName = broker.HostName,
                Port = broker.Port,
                VirtualHost = broker.VirtualHost,
                UserName = broker.UserName,
                Password = broker.Password,
                ExchangeName = exchangeName,
                ClientName = clientName,
                ReconnectDelaySeconds = 1,
                OutstandingPollIntervalMilliseconds = 20,
            };
            configure?.Invoke(config);

            service = new RabbitMQService(eventAggregator, registry);
            service.Init(config);
            ExchangeName = exchangeName;
        }

        public string ExchangeName { get; }

        public string InstanceId => service.InstanceId;

        public bool IsConnected => service.IsConsumerConnected && service.IsPublisherConnected;

        public Received<TPayload> Listen<TEvent, TPayload>()
            where TEvent : PubSubEvent<TPayload>, new()
            where TPayload : class
        {
            Received<TPayload> received = new Received<TPayload>();
            eventAggregator.GetEvent<TEvent>().Subscribe(received.Add, ThreadOption.PublisherThread, keepSubscriberReferenceAlive: true);
            return received;
        }

        /// <summary>
        /// Does what <c>PublishRemote</c> does on <typeparamref name="TEvent"/>, a plain <see cref="PubSubEvent{TPayload}"/>.
        /// Several apps share this process, so this one's service is used directly instead of <see cref="RabbitMQServiceProvider"/>.
        /// </summary>
        public void PublishRemote<TEvent, TPayload>(TPayload payload)
            where TEvent : PubSubEvent<TPayload>, new()
        {
            service.Publish(typeof(TEvent), payload);
            eventAggregator.GetEvent<TEvent>().Publish(payload);
        }

        /// <summary>Does what <c>PublishRemote</c> with a routing key does on <typeparamref name="TEvent"/>.</summary>
        public void PublishRemoteTo<TEvent, TPayload>(string routingKey, TPayload payload)
            where TEvent : PubSubEvent<TPayload>, new()
        {
            service.Publish(typeof(TEvent), payload, routingKey);
            eventAggregator.GetEvent<TEvent>().Publish(payload);
        }

        public void Dispose()
        {
            service.Dispose();
        }
    }
}
