using System;
using System.Collections.Generic;
using System.Reflection;
using Common.RabbitMQ.Tests.Broker;
using Prism.Events;
using LegacyConfig = Legacy.RabbitMQ.RabbitMQConfig;
using LegacyExtensions = Legacy.RabbitMQ.PubSubEventExtensions;
using LegacyRegistry = Legacy.RabbitMQ.RemoteEventRegistry;
using LegacyService = Legacy.RabbitMQ.RabbitMQService;

namespace Common.RabbitMQ.Tests.LegacyModel
{
    /// <summary>
    /// One running legacy application, built on the legacy model: its own exchange, its subscriptions, its event assemblies.
    /// The model's types go through aliases: inside this namespace an unqualified RabbitMQConfig would bind to the current build.
    /// </summary>
    public sealed class LegacyApp : IDisposable
    {
        private readonly EventAggregator eventAggregator = new EventAggregator();
        private readonly LegacyService service;

        public LegacyApp(BrokerSettings broker, string exchangeName, IEnumerable<string> subscribeTo, string clientName, Assembly[] eventAssemblies)
        {
            service = new LegacyService(eventAggregator, new LegacyRegistry(eventAssemblies));
            service.Init(new LegacyConfig
            {
                HostName = broker.HostName,
                Port = broker.Port,
                VirtualHost = broker.VirtualHost,
                UserName = broker.UserName,
                Password = broker.Password,
                ExchangeName = exchangeName,
                SubscribeTo = new List<string>(subscribeTo),
                ClientName = clientName,
                ReconnectDelaySeconds = 1,
                OutstandingPollIntervalMilliseconds = 20,
            });
            ExchangeName = exchangeName;
            ClientName = clientName;
        }

        public string ExchangeName { get; }

        public string ClientName { get; }

        public string InstanceId => service.InstanceId;

        public string QueueName => service.QueueName;

        public bool IsConnected => service.IsConsumerConnected && service.IsPublisherConnected;

        /// <summary>Subscribes to <typeparamref name="TEvent"/> on this app's aggregator; call before messages arrive.</summary>
        public Received<TPayload> Listen<TEvent, TPayload>()
            where TEvent : PubSubEvent<TPayload>, new()
            where TPayload : class
        {
            Received<TPayload> received = new Received<TPayload>();
            eventAggregator.GetEvent<TEvent>().Subscribe(received.Add, ThreadOption.PublisherThread, keepSubscriberReferenceAlive: true);
            return received;
        }

        /// <summary><c>GetEvent&lt;TEvent&gt;().PublishRemote(payload, service)</c>.</summary>
        public void PublishRemote<TEvent, TPayload>(TPayload payload)
            where TEvent : PubSubEvent<TPayload>, new()
        {
            LegacyExtensions.PublishRemote(eventAggregator.GetEvent<TEvent>(), payload, service);
        }

        public void Dispose()
        {
            service.Dispose();
        }
    }
}
