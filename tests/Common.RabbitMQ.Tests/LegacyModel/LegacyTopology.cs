using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Common.RabbitMQ.Tests.Broker;
using Messaging;
using Messaging.RabbitMQ;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Common.RabbitMQ.Tests.LegacyModel
{
    /// <summary>
    /// One test's set of legacy applications. Each application gets an exchange named after it plus a per-test suffix,
    /// so tests don't share exchanges. Disposing stops the applications and deletes their (durable) exchanges.
    /// </summary>
    public sealed class LegacyTopology : IAsyncDisposable
    {
        private readonly List<LegacyApp> apps = new List<LegacyApp>();
        private readonly List<CoreApp> coreApps = new List<CoreApp>();
        private readonly List<AdapterApp> adapterApps = new List<AdapterApp>();
        private readonly List<string> durableQueues = new List<string>();
        private readonly HashSet<string> exchanges = new HashSet<string>(StringComparer.Ordinal);
        private readonly string suffix = Guid.NewGuid().ToString("N").Substring(0, 12);
        private readonly IConnection connection;
        private readonly IChannel channel;

        private LegacyTopology(BrokerSettings broker, IConnection connection, IChannel channel)
        {
            Broker = broker;
            this.connection = connection;
            this.channel = channel;
        }

        public BrokerSettings Broker { get; }

        /// <summary>Skips the test when there is no broker (see <see cref="BrokerSettings"/>).</summary>
        public static async Task<LegacyTopology> Create()
        {
            BrokerSettings broker = await BrokerSettings.Require().ConfigureAwait(false);
            IConnection connection = await broker.CreateConnectionFactory().CreateConnectionAsync("Common.RabbitMQ.Tests legacy topology").ConfigureAwait(false);
            IChannel channel = await connection.CreateChannelAsync().ConfigureAwait(false);
            return new LegacyTopology(broker, connection, channel);
        }

        /// <summary>The exchange of application <paramref name="app"/> in this test.</summary>
        public string Exchange(string app)
        {
            string name = $"{app}.{suffix}";
            exchanges.Add(name);
            return name;
        }

        /// <summary>Starts an instance of application <paramref name="app"/>, receiving from the exchanges of <paramref name="subscribeTo"/>.</summary>
        public LegacyApp Start(string app, string[] subscribeTo, params Assembly[] eventAssemblies)
        {
            LegacyApp instance = new LegacyApp(Broker, Exchange(app), subscribeTo.Select(Exchange), app, eventAssemblies);
            apps.Add(instance);
            return instance;
        }

        /// <summary>
        /// Creates an application on the core with its own exchange named after <paramref name="app"/>. Register its message
        /// types with <see cref="CoreApp.Listen{T}"/>, then call <see cref="CoreApp.Start"/>.
        /// </summary>
        public CoreApp CreateCore(string app, Action<RabbitMQBusOptions> configure = null, IMessagingObserver observer = null)
        {
            CoreApp instance = new CoreApp(Broker, Exchange(app), app, configure, observer);
            coreApps.Add(instance);
            return instance;
        }

        /// <summary>
        /// Starts an application on the current <c>Common.RabbitMQ</c> adapter with its own exchange named after
        /// <paramref name="app"/> and its events registered by assembly.
        /// </summary>
        public AdapterApp StartAdapter(string app, Action<global::Common.RabbitMQ.RabbitMQConfig> configure, params Assembly[] eventAssemblies)
        {
            AdapterApp instance = new AdapterApp(Broker, Exchange(app), app, configure, eventAssemblies);
            adapterApps.Add(instance);
            return instance;
        }

        public Task WaitUntilConnected(params AdapterApp[] which)
        {
            return WaitUntil(() => which.All(a => a.IsConnected));
        }

        /// <summary>Deletes durable (shared) queues when the topology is disposed.</summary>
        public void DeleteOnDispose(params string[] queueNames)
        {
            durableQueues.AddRange(queueNames.Where(q => q != null && !durableQueues.Contains(q)));
        }

        public Task WaitUntilConnected(params LegacyApp[] which)
        {
            return WaitUntil(() => which.All(a => a.IsConnected));
        }

        public Task WaitUntilConnected(params CoreApp[] which)
        {
            return WaitUntil(() => which.All(a => a.IsConnected));
        }

        /// <summary>Takes one message off <paramref name="queueName"/>, or null when it is empty.</summary>
        public async Task<CapturedMessage> Get(string queueName)
        {
            BasicGetResult result = await channel.BasicGetAsync(queueName, autoAck: true).ConfigureAwait(false);
            return result == null ? null : WireCapture.Copy(result.Exchange, result.RoutingKey, result.BasicProperties, result.Body);
        }

        private static async Task WaitUntil(Func<bool> connected)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (!connected())
            {
                if (stopwatch.Elapsed > TestBus.Timeout)
                {
                    throw new TimeoutException($"Apps did not connect within {TestBus.Timeout.TotalSeconds}s.");
                }

                await Task.Delay(50).ConfigureAwait(false);
            }
        }

        /// <summary>Everything published to <paramref name="app"/>'s exchange. The exchange must exist.</summary>
        public Task<WireCapture> Capture(string app)
        {
            return WireCapture.Start(channel, Exchange(app));
        }

        /// <summary>Publishes to <paramref name="app"/>'s exchange as another program would, without the legacy library.</summary>
        public async Task PublishRaw(string app, string routingKey, IDictionary<string, object> headers, byte[] body)
        {
            BasicProperties properties = new BasicProperties { ContentType = "application/json", Headers = headers };
            await channel.BasicPublishAsync(Exchange(app), routingKey, mandatory: false, basicProperties: properties, body: body).ConfigureAwait(false);
        }

        /// <summary>
        /// True when a message with <paramref name="routingKey"/> on <paramref name="app"/>'s exchange reaches at least one
        /// queue, i.e. some queue has a matching binding. Uses a mandatory publish: an unroutable message comes back.
        /// </summary>
        public async Task<bool> IsRoutable(string app, string routingKey)
        {
            CreateChannelOptions options = new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true);
            using (IChannel probe = await connection.CreateChannelAsync(options).ConfigureAwait(false))
            {
                try
                {
                    await probe.BasicPublishAsync(Exchange(app), routingKey, mandatory: true, basicProperties: new BasicProperties(), body: new byte[0]).ConfigureAwait(false);
                    return true;
                }
                catch (PublishException ex) when (ex.IsReturn)
                {
                    return false;
                }
            }
        }

        /// <summary>Declares an exchange with the given arguments on a throwaway channel.</summary>
        /// <returns>200 when it exists with exactly these arguments (or was created), otherwise the broker's reply code (406 = different arguments).</returns>
        public Task<ushort> DeclareExchange(string exchangeName, string type, bool durable)
        {
            return OnThrowawayChannel(probe => probe.ExchangeDeclareAsync(exchangeName, type, durable, autoDelete: false));
        }

        /// <returns>200 when the exchange exists, 404 when it doesn't.</returns>
        public Task<ushort> ExchangeExists(string exchangeName)
        {
            return OnThrowawayChannel(probe => probe.ExchangeDeclarePassiveAsync(exchangeName));
        }

        /// <returns>200 when the queue exists and is usable, 404 not found, 405 exclusive to another connection.</returns>
        public Task<ushort> PassiveDeclareQueue(string queueName)
        {
            return OnThrowawayChannel(async probe => await probe.QueueDeclarePassiveAsync(queueName).ConfigureAwait(false));
        }

        public async ValueTask DisposeAsync()
        {
            foreach (LegacyApp app in apps)
            {
                app.Dispose();
            }

            foreach (CoreApp app in coreApps)
            {
                app.Dispose();
            }

            foreach (AdapterApp app in adapterApps)
            {
                app.Dispose();
            }

            try
            {
                foreach (string queueName in durableQueues)
                {
                    await channel.QueueDeleteAsync(queueName).ConfigureAwait(false);
                }

                foreach (string exchange in exchanges)
                {
                    await channel.ExchangeDeleteAsync(exchange).ConfigureAwait(false);
                }

                await channel.CloseAsync().ConfigureAwait(false);
                await connection.CloseAsync().ConfigureAwait(false);
            }
            finally
            {
                channel.Dispose();
                connection.Dispose();
            }
        }

        /// <summary>A failed declare closes its channel, so each check gets its own.</summary>
        private async Task<ushort> OnThrowawayChannel(Func<IChannel, Task> action)
        {
            using (IChannel probe = await connection.CreateChannelAsync().ConfigureAwait(false))
            {
                try
                {
                    await action(probe).ConfigureAwait(false);
                    return 200;
                }
                catch (OperationInterruptedException ex) when (ex.ShutdownReason != null)
                {
                    return ex.ShutdownReason.ReplyCode;
                }
            }
        }
    }
}
