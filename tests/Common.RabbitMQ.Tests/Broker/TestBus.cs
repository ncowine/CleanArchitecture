using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Messaging;
using Messaging.RabbitMQ;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Common.RabbitMQ.Tests.Broker
{
    /// <summary>
    /// One test's private bus: a uniquely named exchange on the test broker, the endpoints joined to it and a raw
    /// connection for capturing and publishing. Disposing stops the endpoints and deletes the exchange.
    /// </summary>
    public sealed class TestBus : IAsyncDisposable
    {
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        private readonly List<ICompatEndpoint> endpoints = new List<ICompatEndpoint>();
        private readonly List<string> durableQueues = new List<string>();
        private readonly BrokerSettings broker;
        private readonly IConnection connection;
        private readonly IChannel channel;

        private TestBus(BrokerSettings broker, string exchangeName, IConnection connection, IChannel channel)
        {
            this.broker = broker;
            this.connection = connection;
            this.channel = channel;
            ExchangeName = exchangeName;
        }

        public string ExchangeName { get; }

        public BrokerSettings Broker => broker;

        /// <summary>Skips the test when there is no broker (see <see cref="BrokerSettings"/>).</summary>
        public static async Task<TestBus> Create()
        {
            BrokerSettings broker = await BrokerSettings.Require().ConfigureAwait(false);
            string exchangeName = $"compat.test.{Guid.NewGuid():N}";

            IConnection connection = await broker.CreateConnectionFactory().CreateConnectionAsync("Common.RabbitMQ.Tests").ConfigureAwait(false);
            IChannel channel = await connection.CreateChannelAsync().ConfigureAwait(false);

            // Declared with the frozen shape up front. A build that declares it differently gets PRECONDITION_FAILED
            // and never connects, so every test on this bus also checks the exchange type and durability.
            await channel.ExchangeDeclareAsync(exchangeName, ExchangeType.Topic, durable: true, autoDelete: false).ConfigureAwait(false);

            return new TestBus(broker, exchangeName, connection, channel);
        }

        public ICompatEndpoint AddEndpoint(CompatBuild build, string clientName)
        {
            switch (build)
            {
                case CompatBuild.Current:
                    return Track(new CurrentEndpoint(broker, ExchangeName, clientName));
                case CompatBuild.Baseline:
                    return Track(new BaselineEndpoint(broker, ExchangeName, clientName));
                default:
                    return AddCoreEndpoint(clientName, null);
            }
        }

        /// <param name="observer">Null for the bus's default: no observer, no extra headers.</param>
        /// <param name="configure">Changes the test defaults, for example to use a shared queue.</param>
        public CoreEndpoint AddCoreEndpoint(string clientName, IMessagingObserver observer, Action<RabbitMQBusOptions> configure = null)
        {
            CoreEndpoint endpoint = Track(new CoreEndpoint(broker, ExchangeName, clientName, observer, configure));
            if (endpoint.Bus.DeadLetterQueueName != null)
            {
                // Shared queues are durable: they outlive the test unless deleted.
                DeleteOnDispose(endpoint.Bus.QueueName, endpoint.Bus.DeadLetterQueueName);
            }

            return endpoint;
        }

        /// <summary>Deletes durable queues when the bus is disposed.</summary>
        public void DeleteOnDispose(params string[] queueNames)
        {
            foreach (string queueName in queueNames)
            {
                if (!durableQueues.Contains(queueName))
                {
                    durableQueues.Add(queueName);
                }
            }
        }

        /// <summary>Waits until every endpoint has its consumer queue bound and its publisher open.</summary>
        public Task WaitUntilConnected()
        {
            return WaitUntilConnected(endpoints.ToArray());
        }

        public async Task WaitUntilConnected(params ICompatEndpoint[] which)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (!Array.TrueForAll(which, e => e.IsConnected))
            {
                if (stopwatch.Elapsed > Timeout)
                {
                    throw new TimeoutException($"Endpoints did not connect to exchange '{ExchangeName}' within {Timeout.TotalSeconds}s.");
                }

                await Task.Delay(50).ConfigureAwait(false);
            }
        }

        public Task<WireCapture> Capture()
        {
            return WireCapture.Start(channel, ExchangeName);
        }

        /// <summary>Publishes as another application would, without either build.</summary>
        public async Task PublishRaw(string routingKey, IDictionary<string, object> headers, byte[] body)
        {
            BasicProperties properties = new BasicProperties
            {
                ContentType = "application/json",
                MessageId = Guid.NewGuid().ToString("N"),
                Headers = headers,
            };

            await channel.BasicPublishAsync(ExchangeName, routingKey, mandatory: false, basicProperties: properties, body: body).ConfigureAwait(false);
        }

        /// <summary>Takes one message off <paramref name="queueName"/>, or null when it is empty.</summary>
        public async Task<CapturedMessage> Get(string queueName)
        {
            BasicGetResult result = await channel.BasicGetAsync(queueName, autoAck: true).ConfigureAwait(false);
            return result == null ? null : WireCapture.Copy(result.Exchange, result.RoutingKey, result.BasicProperties, result.Body);
        }

        /// <summary>
        /// Passively declares <paramref name="queueName"/> from this test's own connection.
        /// </summary>
        /// <returns>200 when the queue exists and is usable, otherwise the broker's reply code (404 not found, 405 locked).</returns>
        public async Task<ushort> PassiveDeclare(string queueName)
        {
            // A failed passive declare closes the channel, so use a throwaway one.
            using (IChannel probe = await connection.CreateChannelAsync().ConfigureAwait(false))
            {
                try
                {
                    await probe.QueueDeclarePassiveAsync(queueName).ConfigureAwait(false);
                    return 200;
                }
                catch (OperationInterruptedException ex) when (ex.ShutdownReason != null)
                {
                    return ex.ShutdownReason.ReplyCode;
                }
            }
        }

        private T Track<T>(T endpoint) where T : ICompatEndpoint
        {
            endpoints.Add(endpoint);
            return endpoint;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (ICompatEndpoint endpoint in endpoints)
            {
                endpoint.Dispose();
            }

            try
            {
                foreach (string queueName in durableQueues)
                {
                    await channel.QueueDeleteAsync(queueName).ConfigureAwait(false);
                }

                await channel.ExchangeDeleteAsync(ExchangeName).ConfigureAwait(false);
                await channel.CloseAsync().ConfigureAwait(false);
                await connection.CloseAsync().ConfigureAwait(false);
            }
            finally
            {
                channel.Dispose();
                connection.Dispose();
            }
        }
    }
}
