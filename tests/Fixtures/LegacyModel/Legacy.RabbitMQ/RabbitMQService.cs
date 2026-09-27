using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Prism.Events;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Legacy.RabbitMQ
{
    /// <summary>
    /// Model of the legacy service: one application, one connection, its own exchange plus the exchanges it subscribes to.
    /// <see cref="Init"/> starts three fire-and-forget loops: <see cref="ConnectConsumer"/>, <see cref="ConnectPublisher"/>
    /// and <see cref="OutstandingQueue"/>. Received messages are raised as their Prism event on the aggregator.
    /// Assumption IDs refer to docs/legacy-baseline-assumptions.md.
    /// </summary>
    public class RabbitMQService : IRabbitMQService
    {
        private readonly IEventAggregator eventAggregator;
        private readonly RemoteEventRegistry registry;
        private readonly ConcurrentQueue<OutgoingMessage> outstandingMessages = new ConcurrentQueue<OutgoingMessage>();
        private readonly ConcurrentDictionary<string, RemoteEventDescriptor> registeredEvents =
            new ConcurrentDictionary<string, RemoteEventDescriptor>(StringComparer.Ordinal);
        private readonly SemaphoreSlim consumerLock = new SemaphoreSlim(1, 1);
        private readonly string instanceId = Guid.NewGuid().ToString("N");

        private RabbitMQConfig config;
        private CancellationTokenSource cancellation;
        private IConnection consumerConnection;
        private IChannel consumerChannel;
        private string consumerQueueName;
        private IConnection publisherConnection;
        private IChannel publisherChannel;
        private bool disposed;

        public RabbitMQService(IEventAggregator eventAggregator, RemoteEventRegistry registry)
        {
            this.eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public event EventHandler<string> Log;

        public string InstanceId => instanceId;

        /// <summary>Assumption A7: one queue per running process.</summary>
        public string QueueName => config == null ? null : $"{config.ClientName}.{instanceId}".ToLowerInvariant();

        public bool IsConsumerConnected => IsOpen(consumerConnection, consumerChannel);

        public bool IsPublisherConnected => IsOpen(publisherConnection, publisherChannel);

        public void Init(RabbitMQConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (this.config != null)
            {
                throw new InvalidOperationException("RabbitMQService is already initialized.");
            }

            if (string.IsNullOrWhiteSpace(config.ExchangeName))
            {
                throw new ArgumentException("ExchangeName is required.", nameof(config));
            }

            if (string.IsNullOrWhiteSpace(config.ClientName))
            {
                config.ClientName = AppDomain.CurrentDomain.FriendlyName;
            }

            this.config = config;

            // Assumption A8: every known event is received, from every exchange (own and subscribed).
            foreach (RemoteEventDescriptor descriptor in registry.Events)
            {
                registeredEvents[descriptor.EventName] = descriptor;
            }

            cancellation = new CancellationTokenSource();
            CancellationToken token = cancellation.Token;

            Task.Run(() => ConnectConsumer(token));
            Task.Run(() => ConnectPublisher(token));
            Task.Run(() => OutstandingQueue(token));

            WriteLog($"Initialized with {registeredEvents.Count} event(s), exchange '{config.ExchangeName}', subscribed to {config.SubscribeTo.Count} exchange(s).");
        }

        public void Publish(Type eventType, object payload)
        {
            EnsureInitialized();

            if (eventType == null)
            {
                throw new ArgumentNullException(nameof(eventType));
            }

            if (!registry.TryGet(eventType, out RemoteEventDescriptor descriptor))
            {
                throw new ArgumentException($"{eventType.FullName} is not in a registered event assembly.", nameof(eventType));
            }

            // Assumption A5: the payload alone, Newtonsoft default settings, serialized now.
            byte[] body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
            outstandingMessages.Enqueue(new OutgoingMessage(descriptor.EventName, body));
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            if (cancellation == null)
            {
                return;
            }

            cancellation.Cancel();

            try
            {
                Task.Run(async () =>
                {
                    await CloseConsumer().ConfigureAwait(false);
                    await ClosePublisher().ConfigureAwait(false);
                }).Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                WriteLog($"Error while closing connections: {ex.Message}");
            }

            cancellation.Dispose();
        }

        #region Consumer

        private async Task ConnectConsumer(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!IsOpen(consumerConnection, consumerChannel))
                    {
                        await CloseConsumer().ConfigureAwait(false);
                        await OpenConsumer(token).ConfigureAwait(false);
                        WriteLog($"Consumer connected, queue '{consumerQueueName}'.");
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    WriteLog($"Consumer connection failed: {ex.Message}");
                }

                if (!await Delay(TimeSpan.FromSeconds(config.ReconnectDelaySeconds), token).ConfigureAwait(false))
                {
                    break;
                }
            }
        }

        private async Task OpenConsumer(CancellationToken token)
        {
            await consumerLock.WaitAsync(token).ConfigureAwait(false);

            IConnection connection = null;
            IChannel channel = null;
            try
            {
                connection = await CreateConnectionFactory()
                    .CreateConnectionAsync($"{config.ClientName} consumer", token)
                    .ConfigureAwait(false);

                channel = await connection.CreateChannelAsync(cancellationToken: token).ConfigureAwait(false);

                // Assumption A1: the application declares its own exchange. Assumption A16: it also declares the
                // exchanges it subscribes to, the same way, so it can bind before their owners have started.
                List<string> exchanges = new List<string> { config.ExchangeName };
                exchanges.AddRange(config.SubscribeTo.Where(e => !string.Equals(e, config.ExchangeName, StringComparison.Ordinal)));
                foreach (string exchange in exchanges)
                {
                    await DeclareExchange(channel, exchange, token).ConfigureAwait(false);
                }

                await channel.BasicQosAsync(0, (ushort)config.PrefetchCount, false, token).ConfigureAwait(false);

                // Assumption A7: one exclusive, auto-delete queue per running process.
                QueueDeclareOk queue = await channel.QueueDeclareAsync(
                    queue: QueueName,
                    durable: false,
                    exclusive: true,
                    autoDelete: true,
                    cancellationToken: token).ConfigureAwait(false);

                // Assumptions A8 and A9: one binding per known event, on every exchange, routing key = full type name.
                foreach (string exchange in exchanges)
                {
                    foreach (RemoteEventDescriptor descriptor in registeredEvents.Values)
                    {
                        await channel.QueueBindAsync(queue.QueueName, exchange, descriptor.EventName, cancellationToken: token).ConfigureAwait(false);
                    }
                }

                AsyncEventingBasicConsumer consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += OnMessageReceived;
                await channel.BasicConsumeAsync(queue.QueueName, autoAck: false, consumer: consumer, cancellationToken: token).ConfigureAwait(false);

                consumerConnection = connection;
                consumerChannel = channel;
                consumerQueueName = queue.QueueName;
            }
            catch
            {
                await CloseQuietly(channel, connection).ConfigureAwait(false);
                throw;
            }
            finally
            {
                consumerLock.Release();
            }
        }

        private async Task CloseConsumer()
        {
            IConnection connection;
            IChannel channel;

            await consumerLock.WaitAsync().ConfigureAwait(false);
            try
            {
                connection = consumerConnection;
                channel = consumerChannel;
                consumerConnection = null;
                consumerChannel = null;
                consumerQueueName = null;
            }
            finally
            {
                consumerLock.Release();
            }

            await CloseQuietly(channel, connection).ConfigureAwait(false);
        }

        private async Task OnMessageReceived(object sender, BasicDeliverEventArgs args)
        {
            IChannel channel = ((AsyncEventingBasicConsumer)sender).Channel;

            try
            {
                IDictionary<string, object> headers = args.BasicProperties?.Headers;

                // Assumption A11: our own message coming back; local subscribers already had it.
                if (ReadHeader(headers, MessageHeaders.SourceId) == instanceId)
                {
                    return;
                }

                // Assumption A10: the event-type header decides the event; the routing key is the fallback.
                string eventName = ReadHeader(headers, MessageHeaders.EventType) ?? args.RoutingKey;
                if (!registeredEvents.TryGetValue(eventName, out RemoteEventDescriptor descriptor))
                {
                    WriteLog($"Ignored unknown event '{eventName}'.");
                    return;
                }

                object payload = JsonConvert.DeserializeObject(Encoding.UTF8.GetString(args.Body.ToArray()), descriptor.PayloadType);
                descriptor.PublishLocal(eventAggregator, payload);
            }
            catch (Exception ex)
            {
                // Assumption A12: acked anyway, so a bad message can't be redelivered forever.
                WriteLog($"Failed to handle message '{args.RoutingKey}': {ex.Message}");
            }
            finally
            {
                try
                {
                    if (channel.IsOpen)
                    {
                        await channel.BasicAckAsync(args.DeliveryTag, false).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    WriteLog($"Failed to ack message: {ex.Message}");
                }
            }
        }

        #endregion

        #region Publisher

        private async Task ConnectPublisher(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!IsOpen(publisherConnection, publisherChannel))
                    {
                        await ClosePublisher().ConfigureAwait(false);
                        await OpenPublisher(token).ConfigureAwait(false);
                        WriteLog("Publisher connected.");
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    WriteLog($"Publisher connection failed: {ex.Message}");
                }

                if (!await Delay(TimeSpan.FromSeconds(config.ReconnectDelaySeconds), token).ConfigureAwait(false))
                {
                    break;
                }
            }
        }

        private async Task OpenPublisher(CancellationToken token)
        {
            IConnection connection = null;
            IChannel channel = null;
            try
            {
                connection = await CreateConnectionFactory()
                    .CreateConnectionAsync($"{config.ClientName} publisher", token)
                    .ConfigureAwait(false);

                // Assumption A6: publisher confirms; a message leaves the outstanding queue once the broker has it.
                CreateChannelOptions options = new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true);
                channel = await connection.CreateChannelAsync(options, token).ConfigureAwait(false);
                await DeclareExchange(channel, config.ExchangeName, token).ConfigureAwait(false);

                publisherConnection = connection;
                publisherChannel = channel;
            }
            catch
            {
                await CloseQuietly(channel, connection).ConfigureAwait(false);
                throw;
            }
        }

        private async Task ClosePublisher()
        {
            IConnection connection = publisherConnection;
            IChannel channel = publisherChannel;
            publisherConnection = null;
            publisherChannel = null;

            await CloseQuietly(channel, connection).ConfigureAwait(false);
        }

        #endregion

        #region Outstanding queue

        private async Task OutstandingQueue(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    IChannel channel = publisherChannel;
                    if (channel != null && channel.IsOpen && outstandingMessages.TryPeek(out OutgoingMessage message))
                    {
                        await PublishMessage(channel, message, token).ConfigureAwait(false);
                        outstandingMessages.TryDequeue(out OutgoingMessage published);
                        continue;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    WriteLog($"Publish failed, message kept in outstanding queue: {ex.Message}");
                }

                if (!await Delay(TimeSpan.FromMilliseconds(config.OutstandingPollIntervalMilliseconds), token).ConfigureAwait(false))
                {
                    break;
                }
            }
        }

        private async Task PublishMessage(IChannel channel, OutgoingMessage message, CancellationToken token)
        {
            // Assumptions A3 and A4: headers and properties.
            BasicProperties properties = new BasicProperties
            {
                ContentType = "application/json",
                MessageId = message.MessageId,
                AppId = config.ClientName,
                Timestamp = new AmqpTimestamp(message.CreatedUtc.ToUnixTimeSeconds()),
                Headers = new Dictionary<string, object>
                {
                    [MessageHeaders.EventType] = message.EventName,
                    [MessageHeaders.SourceId] = instanceId,
                },
            };

            // Fact F2 and assumption A2: to this application's own exchange, routing key = full type name.
            await channel.BasicPublishAsync(
                exchange: config.ExchangeName,
                routingKey: message.EventName,
                mandatory: false,
                basicProperties: properties,
                body: message.Body,
                cancellationToken: token).ConfigureAwait(false);
        }

        #endregion

        #region Helpers

        private ConnectionFactory CreateConnectionFactory()
        {
            return new ConnectionFactory
            {
                HostName = config.HostName,
                Port = config.Port,
                VirtualHost = config.VirtualHost,
                UserName = config.UserName,
                Password = config.Password,
                RequestedHeartbeat = TimeSpan.FromSeconds(config.HeartbeatSeconds),

                // Assumption A13: reconnecting is done by the connect loops, not by the client library.
                AutomaticRecoveryEnabled = false,
                TopologyRecoveryEnabled = false,
            };
        }

        /// <summary>Assumption A1: topic, durable, not auto-delete.</summary>
        private static Task DeclareExchange(IChannel channel, string exchange, CancellationToken token)
        {
            return channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: token);
        }

        private void EnsureInitialized()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(RabbitMQService));
            }

            if (config == null)
            {
                throw new InvalidOperationException("RabbitMQService is not initialized. Call Init first.");
            }
        }

        private void WriteLog(string message)
        {
            Trace.WriteLine($"[RabbitMQ:{config?.ExchangeName}] {message}");
            Log?.Invoke(this, message);
        }

        private static bool IsOpen(IConnection connection, IChannel channel)
        {
            return connection != null && connection.IsOpen && channel != null && channel.IsOpen;
        }

        private static string ReadHeader(IDictionary<string, object> headers, string name)
        {
            if (headers == null || !headers.TryGetValue(name, out object value) || value == null)
            {
                return null;
            }

            return value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : value.ToString();
        }

        /// <returns>False when cancelled.</returns>
        private static async Task<bool> Delay(TimeSpan delay, CancellationToken token)
        {
            try
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        private static async Task CloseQuietly(IChannel channel, IConnection connection)
        {
            try
            {
                if (channel != null)
                {
                    if (channel.IsOpen)
                    {
                        await channel.CloseAsync().ConfigureAwait(false);
                    }

                    channel.Dispose();
                }
            }
            catch
            {
                // Already broken; nothing to do.
            }

            try
            {
                if (connection != null)
                {
                    if (connection.IsOpen)
                    {
                        await connection.CloseAsync().ConfigureAwait(false);
                    }

                    connection.Dispose();
                }
            }
            catch
            {
                // Already broken; nothing to do.
            }
        }

        #endregion
    }
}
