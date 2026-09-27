using System;
using System.Threading;
using System.Threading.Tasks;
using Compat.Events;
using Messaging;
using Messaging.RabbitMQ;

namespace Common.RabbitMQ.Tests.Broker
{
    /// <summary>A bare <see cref="RabbitMQBus"/> subscribed to <see cref="CompatEvent"/>'s wire name, with an optional observer.</summary>
    public sealed class CoreEndpoint : ICompatEndpoint, IInboundDispatcher
    {
        public static readonly string WireName = typeof(CompatEvent).FullName;

        private readonly RabbitMQBus bus;
        private int failuresLeft = int.MaxValue;
        private int attempts;

        /// <param name="configure">Changes the test defaults, for example to use a shared queue.</param>
        public CoreEndpoint(BrokerSettings broker, string exchangeName, string clientName, IMessagingObserver observer, Action<RabbitMQBusOptions> configure = null)
        {
            RabbitMQBusOptions options = new RabbitMQBusOptions
            {
                BusName = CompatBus.Name,
                HostName = broker.HostName,
                Port = broker.Port,
                VirtualHost = broker.VirtualHost,
                UserName = broker.UserName,
                Password = broker.Password,
                ExchangeName = exchangeName,
                ClientName = clientName,
                ReconnectDelay = TimeSpan.FromSeconds(1),
                OutstandingPollInterval = TimeSpan.FromMilliseconds(20),
                RetryDelay = TimeSpan.FromMilliseconds(50),
            };
            configure?.Invoke(options);

            bus = new RabbitMQBus(options, TestJsonSerializer.Instance, this, observer);
            bus.Start(new[] { new MessageRegistration(WireName, typeof(CompatPayload)) });
        }

        public CompatBuild Build => CompatBuild.Core;

        public RabbitMQBus Bus => bus;

        public string InstanceId => bus.InstanceId;

        public string QueueName => bus.QueueName;

        public bool IsConnected => bus.IsConsumerConnected && bus.IsPublisherConnected;

        public ReceivedPayloads Received { get; } = new ReceivedPayloads();

        /// <summary>When set, the dispatcher throws it instead of receiving the message (see <see cref="FailTimes"/>).</summary>
        public Exception HandlerException { get; set; }

        /// <summary>How many times <see cref="HandlerException"/> is thrown before messages succeed. Unlimited by default.</summary>
        public int FailTimes
        {
            set => Volatile.Write(ref failuresLeft, value);
        }

        /// <summary>Every call to the dispatcher, failed or not.</summary>
        public int Attempts => Volatile.Read(ref attempts);

        /// <summary>Only sends: there are no local subscribers.</summary>
        public void PublishRemote(CompatPayload payload)
        {
            bus.Enqueue(WireName, payload);
        }

        public Task Dispatch(MessageRegistration registration, object message, MessageContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref attempts);

            if (HandlerException != null && Interlocked.Decrement(ref failuresLeft) >= 0)
            {
                throw HandlerException;
            }

            Received.Add((CompatPayload)message);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            bus.Dispose();
        }
    }
}
