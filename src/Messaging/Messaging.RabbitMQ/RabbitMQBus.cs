using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Messaging.RabbitMQ
{
    /// <summary>
    /// One connection to one bus (virtual host + exchange).
    /// <para>
    /// <see cref="Start"/> starts three background loops that run until the bus is disposed: <see cref="KeepConnected"/>
    /// once for the consumer connection (its queue and bindings) and once for the publisher connection, and
    /// <see cref="OutstandingQueue"/>, which sends buffered messages whenever the publisher is connected.
    /// </para>
    /// By default every instance has its own exclusive queue, so every instance receives every message; its own messages
    /// are dropped using the <see cref="WireHeaders.SourceId"/> header. Received messages go to the <see cref="IInboundDispatcher"/>.
    /// </summary>
    public sealed class RabbitMQBus : IDisposable
    {
        private readonly RabbitMQBusOptions options;
        private readonly IInboundDispatcher dispatcher;
        private readonly string instanceId;
        private readonly string queueName;
        private readonly ILogger logger;
        private readonly ConcurrentQueue<PendingMessage> outstandingMessages = new ConcurrentQueue<PendingMessage>();
        private readonly ConcurrentDictionary<string, MessageRegistration> subscriptions =
            new ConcurrentDictionary<string, MessageRegistration>(StringComparer.Ordinal);
        private readonly SemaphoreSlim consumerLock = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();

        // Routing keys subscriptions asked for, counted per (exchange, key). Guarded by bindingGate.
        private readonly Dictionary<(string Exchange, string RoutingKey), int> requestedBindings =
            new Dictionary<(string Exchange, string RoutingKey), int>();
        private readonly object bindingGate = new object();

        // The requested bindings made on the current consumer connection. Only touched while holding consumerLock.
        private readonly HashSet<(string Exchange, string RoutingKey)> appliedBindings = new HashSet<(string Exchange, string RoutingKey)>();

        private IConnection consumerConnection;
        private IChannel consumerChannel;
        private string consumerQueueName;
        private IConnection publisherConnection;
        private IChannel publisherChannel;
        private bool started;
        private bool disposed;

        public RabbitMQBus(RabbitMQBusOptions options, IInboundDispatcher dispatcher, ILogger logger = null)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (string.IsNullOrWhiteSpace(options.BusName) || string.IsNullOrWhiteSpace(options.ExchangeName) || string.IsNullOrWhiteSpace(options.ClientName))
            {
                throw new ArgumentException("BusName, ExchangeName and ClientName are required.", nameof(options));
            }

            if (string.IsNullOrWhiteSpace(options.ExchangeType))
            {
                throw new ArgumentException("ExchangeType is required.", nameof(options));
            }

            if (options.Subscriptions.Any(s => s == null || string.IsNullOrWhiteSpace(s.Exchange)))
            {
                throw new ArgumentException("Every subscription needs an Exchange.", nameof(options));
            }

            this.options = options;
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.logger = logger ?? NullLogger.Instance;
            instanceId = string.IsNullOrWhiteSpace(options.InstanceId) ? Guid.NewGuid().ToString("N") : options.InstanceId;

            if (options.QueueMode == QueueMode.Shared && (options.MaxAttempts < 1 || options.DeliveryLimit < 1))
            {
                throw new ArgumentException("MaxAttempts and DeliveryLimit must be at least 1.", nameof(options));
            }

            queueName = options.QueueMode == QueueMode.Shared
                ? (string.IsNullOrWhiteSpace(options.SharedQueueName) ? $"{options.ClientName}.{options.BusName}".ToLowerInvariant() : options.SharedQueueName)
                : $"{options.ClientName}.{options.BusName}.{Environment.UserName}.{instanceId}".ToLowerInvariant();
        }

        public string BusName => options.BusName;

        public string InstanceId => instanceId;

        public bool IsConsumerConnected => IsOpen(consumerConnection, consumerChannel);

        public bool IsPublisherConnected => IsOpen(publisherConnection, publisherChannel);

        public int OutstandingCount => outstandingMessages.Count;

        public string QueueName => queueName;

        public string DeadLetterQueueName => options.QueueMode == QueueMode.Shared ? queueName + ".dead-letter" : null;

        /// <summary>Whether <paramref name="exchange"/> is this bus's own exchange or one of its configured subscriptions.</summary>
        public bool UsesExchange(string exchange)
        {
            return string.Equals(exchange, options.ExchangeName, StringComparison.Ordinal)
                || options.Subscriptions.Any(s => string.Equals(s.Exchange, exchange, StringComparison.Ordinal));
        }

        public void Start(IEnumerable<MessageRegistration> registrations)
        {
            EnsureNotDisposed();

            if (started)
            {
                throw new InvalidOperationException($"Bus '{options.BusName}' is already started.");
            }

            started = true;

            foreach (MessageRegistration registration in registrations ?? Array.Empty<MessageRegistration>())
            {
                subscriptions[registration.WireName] = registration;
            }

            CancellationToken token = cancellation.Token;

            Task.Run(() => KeepConnected(ConnectionRole.Consumer, token));
            Task.Run(() => KeepConnected(ConnectionRole.Publisher, token));
            Task.Run(() => OutstandingQueue(token));
        }

        /// <summary>
        /// Serializes <paramref name="message"/> now and buffers it for publishing as <paramref name="wireName"/>.
        /// Returns immediately; the message is sent once a publisher connection is available.
        /// </summary>
        public void Enqueue(string wireName, object message)
        {
            Enqueue(wireName, message, null);
        }

        /// <summary>
        /// As <see cref="Enqueue(string, object)"/>, routed with <paramref name="routingKey"/> instead of the exchange name.
        /// The wire name still travels in the event-type header, so receivers identify the message either way.
        /// </summary>
        /// <param name="routingKey">Null or empty for the exchange name, as legacy applications publish.</param>
        public void Enqueue(string wireName, object message, string routingKey)
        {
            EnsureNotDisposed();

            outstandingMessages.Enqueue(Prepare(wireName, message, routingKey));
        }

        private PendingMessage Prepare(string wireName, object message, string routingKey)
        {
            if (string.IsNullOrWhiteSpace(wireName))
            {
                throw new ArgumentException("A wire name is required.", nameof(wireName));
            }

            byte[] body = MessageSerializer.Serialize(message);

            string key = string.IsNullOrEmpty(routingKey) ? options.ExchangeName : routingKey;
            string id = Guid.NewGuid().ToString("N");

            return new PendingMessage(wireName, key, id, body);
        }

        /// <summary>
        /// Starts handing <paramref name="registration"/> to the dispatcher. Nothing changes on the broker: the queue's
        /// bindings are per exchange, so every message on a bound exchange already arrives, and those of no subscribed type
        /// are dropped.
        /// </summary>
        public void Subscribe(MessageRegistration registration)
        {
            EnsureNotDisposed();

            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            if (subscriptions.TryAdd(registration.WireName, registration) && started && IsShared)
            {
                logger.LogWarning("[{Bus}] '{WireName}' was subscribed after the bus started. Messages for it that reached this durable queue earlier were dropped: subscribe before starting.",
                    options.BusName, registration.WireName);
            }
        }

        /// <summary>Stops handing <paramref name="wireName"/> to the dispatcher; its messages are dropped from now on.</summary>
        public void Unsubscribe(string wireName)
        {
            EnsureNotDisposed();

            if (wireName != null)
            {
                subscriptions.TryRemove(wireName, out _);
            }
        }

        /// <summary>
        /// Binds the queue to <paramref name="exchange"/> with <paramref name="routingKey"/>, so messages published with that
        /// key arrive. Calls are counted per exchange and key: the first binds, and <see cref="Unbind"/> removes the binding
        /// when the count reaches zero. Done in the background; while disconnected it is done on the next connect.
        /// </summary>
        /// <param name="exchange">Null for the own exchange and every configured subscription.</param>
        public void Bind(string exchange, string routingKey)
        {
            EnsureNotDisposed();

            bool changed = false;
            lock (bindingGate)
            {
                foreach ((string Exchange, string RoutingKey) pair in BindingPairs(exchange, routingKey))
                {
                    requestedBindings.TryGetValue(pair, out int count);
                    requestedBindings[pair] = count + 1;
                    changed |= count == 0;
                }
            }

            if (changed)
            {
                ApplyBindingsInBackground();
            }
        }

        /// <summary>
        /// Undoes one <see cref="Bind"/>. The last one removes the binding, on a shared queue too: every instance reads that
        /// queue, so it stops receiving the key for all of them. The configured bindings are never removed.
        /// </summary>
        public void Unbind(string exchange, string routingKey)
        {
            if (disposed)
            {
                return;
            }

            bool changed = false;
            lock (bindingGate)
            {
                foreach ((string Exchange, string RoutingKey) pair in BindingPairs(exchange, routingKey))
                {
                    if (!requestedBindings.TryGetValue(pair, out int count))
                    {
                        continue;
                    }

                    if (count > 1)
                    {
                        requestedBindings[pair] = count - 1;
                    }
                    else
                    {
                        requestedBindings.Remove(pair);
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                ApplyBindingsInBackground();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            if (!started)
            {
                cancellation.Dispose();
                return;
            }

            cancellation.Cancel();

            if (!outstandingMessages.IsEmpty)
            {
                logger.LogWarning("[{Bus}] Shutting down with {Count} unsent message(s).", options.BusName, outstandingMessages.Count);
            }

            try
            {
                // Run on the thread pool so a UI thread calling Dispose can't deadlock.
                Task.Run(async () =>
                {
                    await CloseConsumer().ConfigureAwait(false);
                    await ClosePublisher().ConfigureAwait(false);
                }).Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[{Bus}] Error while closing connections: {Error}", options.BusName, ex.Message);
            }

            cancellation.Dispose();
        }

        #region Connections

        private async Task KeepConnected(ConnectionRole role, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (role == ConnectionRole.Consumer && !IsConsumerConnected)
                    {
                        await CloseConsumer().ConfigureAwait(false);
                        await OpenConsumer(token).ConfigureAwait(false);
                        logger.LogInformation("[{Bus}] Consumer connected. Queue '{Queue}'.", options.BusName, consumerQueueName);
                    }
                    else if (role == ConnectionRole.Publisher && !IsPublisherConnected)
                    {
                        await ClosePublisher().ConfigureAwait(false);
                        await OpenPublisher(token).ConfigureAwait(false);
                        logger.LogInformation("[{Bus}] Publisher connected.", options.BusName);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[{Bus}] {Role} could not connect: {Error}. Retrying.", options.BusName, role, ex.Message);
                }

                if (!await Delay(options.ReconnectDelay, token).ConfigureAwait(false))
                {
                    break;
                }
            }
        }

        private Task OnConsumerConnectionShutdown(object sender, ShutdownEventArgs args)
        {
            return OnConnectionLost(ConnectionRole.Consumer, args);
        }

        private Task OnPublisherConnectionShutdown(object sender, ShutdownEventArgs args)
        {
            return OnConnectionLost(ConnectionRole.Publisher, args);
        }

        /// <summary>Only reports the loss; <see cref="KeepConnected"/> notices it on its next check and reconnects.</summary>
        private Task OnConnectionLost(ConnectionRole role, ShutdownEventArgs args)
        {
            logger.LogWarning("[{Bus}] {Role} connection lost: {Reason}. Reconnecting.", options.BusName, role, args.ReplyText);
            return Task.CompletedTask;
        }

        #endregion

        #region Consumer

        private async Task OpenConsumer(CancellationToken token)
        {
            await consumerLock.WaitAsync(token).ConfigureAwait(false);

            IConnection connection = null;
            IChannel channel = null;
            try
            {
                connection = await CreateConnectionFactory()
                    .CreateConnectionAsync(ConnectionName(ConnectionRole.Consumer), token)
                    .ConfigureAwait(false);
                connection.ConnectionShutdownAsync += OnConsumerConnectionShutdown;

                channel = await connection.CreateChannelAsync(cancellationToken: token).ConfigureAwait(false);
                await DeclareExchange(channel, token).ConfigureAwait(false);
                foreach (string exchange in SubscribedExchanges())
                {
                    await CheckExchangeExists(channel, exchange, token).ConfigureAwait(false);
                }

                await channel.BasicQosAsync(0, options.PrefetchCount, false, token).ConfigureAwait(false);

                QueueDeclareOk queue = options.QueueMode == QueueMode.Shared
                    ? await DeclareSharedQueue(channel, token).ConfigureAwait(false)
                    : await DeclareInstanceQueue(channel, token).ConfigureAwait(false);

                foreach ((string Exchange, string RoutingKey) binding in Bindings())
                {
                    await channel.QueueBindAsync(queue.QueueName, binding.Exchange, binding.RoutingKey, cancellationToken: token).ConfigureAwait(false);
                }

                appliedBindings.Clear();
                await ApplyBindings(connection, queue.QueueName, token).ConfigureAwait(false);

                AsyncEventingBasicConsumer consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += OnMessageReceived;
                // The tag shows on the queue's consumer list: whose process is reading it. Unique per channel, which is all
                // RabbitMQ requires, because each connect opens a new channel.
                await channel.BasicConsumeAsync(
                    queue: queue.QueueName,
                    autoAck: false,
                    consumerTag: ConnectionName(ConnectionRole.Consumer),
                    consumer: consumer,
                    cancellationToken: token).ConfigureAwait(false);

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

        private Task<QueueDeclareOk> DeclareInstanceQueue(IChannel channel, CancellationToken token)
        {
            return channel.QueueDeclareAsync(
                queue: queueName,
                durable: false,
                exclusive: true,
                autoDelete: true,
                cancellationToken: token);
        }

        private async Task<QueueDeclareOk> DeclareSharedQueue(IChannel channel, CancellationToken token)
        {
            await DeclareDurableQueue(
                channel,
                DeadLetterQueueName,
                new Dictionary<string, object> { ["x-queue-type"] = "quorum" },
                token).ConfigureAwait(false);

            return await DeclareDurableQueue(
                channel,
                queueName,
                new Dictionary<string, object>
                {
                    ["x-queue-type"] = "quorum",
                    ["x-delivery-limit"] = options.DeliveryLimit,
                    ["x-dead-letter-exchange"] = "",
                    ["x-dead-letter-routing-key"] = DeadLetterQueueName,

                    // At-least-once dead-lettering requires reject-publish overflow.
                    ["x-dead-letter-strategy"] = "at-least-once",
                    ["x-overflow"] = "reject-publish",
                },
                token).ConfigureAwait(false);
        }

        /// <summary>
        /// A queue's arguments can't change after it's created, so a changed setting such as
        /// <see cref="RabbitMQBusOptions.DeliveryLimit"/> makes the broker refuse the declaration (PRECONDITION_FAILED).
        /// This says what to do about it instead of passing on the broker's bare reply.
        /// </summary>
        private static async Task<QueueDeclareOk> DeclareDurableQueue(IChannel channel, string name, Dictionary<string, object> arguments, CancellationToken token)
        {
            try
            {
                return await channel.QueueDeclareAsync(
                    queue: name,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: arguments,
                    cancellationToken: token).ConfigureAwait(false);
            }
            catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 406)
            {
                throw new InvalidOperationException(
                    $"Queue '{name}' already exists with different settings ({ex.ShutdownReason.ReplyText}). A queue's settings " +
                    "can't change after it's created. Put the setting (for example DeliveryLimit) back to the value the queue was " +
                    "created with, or delete the queue so it's created again. Deleting it loses the messages in it.",
                    ex);
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

            if (connection != null)
            {
                connection.ConnectionShutdownAsync -= OnConsumerConnectionShutdown;
            }

            await CloseQuietly(channel, connection).ConfigureAwait(false);
        }

        private async Task OnMessageReceived(object sender, BasicDeliverEventArgs args)
        {
            IChannel channel = ((AsyncEventingBasicConsumer)sender).Channel;
            MessageSettlement settlement;

            try
            {
                settlement = await Receive(args).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Failed before the message was identified, e.g. on its headers.
                logger.LogError(ex, "[{Bus}] Failed to handle message '{RoutingKey}': {Error}", options.BusName, args.RoutingKey, ex.Message);
                settlement = FailedSettlement;
            }

            await Settle(channel, args.DeliveryTag, settlement).ConfigureAwait(false);
        }

        private async Task<MessageSettlement> Receive(BasicDeliverEventArgs args)
        {
            CancellationToken token = cancellation.Token;
            Dictionary<string, string> headers = ReadHeaders(args.BasicProperties?.Headers);

            if (IsOwnEcho(headers))
            {
                return MessageSettlement.Ack;
            }

            string wireName = headers.TryGetValue(WireHeaders.MessageType, out string messageType) && messageType != null
                ? messageType
                : args.RoutingKey;
            if (!subscriptions.TryGetValue(wireName, out MessageRegistration registration))
            {
                // Normal: a queue is bound to whole exchanges, so it gets every event on them, wanted or not.
                logger.LogDebug("[{Bus}] Ignored event '{WireName}': nothing here subscribes to it.", options.BusName, wireName);
                return MessageSettlement.Ack;
            }

            MessageContext context = new MessageContext(wireName, options.BusName, args.BasicProperties?.MessageId, args.Redelivered, headers, args.Exchange, args.RoutingKey);

            return await HandleWithRetries(registration, context, args, token).ConfigureAwait(false);
        }

        private async Task<MessageSettlement> HandleWithRetries(MessageRegistration registration, MessageContext context, BasicDeliverEventArgs args, CancellationToken token)
        {
            int attempts = IsShared ? options.MaxAttempts : 1;

            try
            {
                for (int attempt = 1; ; attempt++)
                {
                    // A fresh copy per attempt, so a handler that changed the message doesn't affect the retry.
                    // A body that can't be deserialized is never retried: it fails the message straight away.
                    object message = MessageSerializer.Deserialize(args.Body.ToArray(), registration.MessageType);

                    try
                    {
                        await dispatcher.Dispatch(registration, message, context, args.CancellationToken).ConfigureAwait(false);
                        return MessageSettlement.Ack;
                    }
                    catch (Exception ex) when (attempt < attempts && !token.IsCancellationRequested)
                    {
                        logger.LogWarning(ex, "[{Bus}] Handling {WireName} {MessageId} (attempt {Attempt} of {Attempts}) failed; retrying: {Error}", options.BusName, context.WireName, context.MessageId, attempt, attempts, ex.Message);
                    }

                    if (!await Delay(options.RetryDelay, token).ConfigureAwait(false))
                    {
                        // Stopping: leave it unacknowledged, so the broker gives it to another instance or the next start.
                        return MessageSettlement.None;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{Bus}] Handling {WireName} {MessageId} failed; the message {Outcome}: {Error}", options.BusName, context.WireName, context.MessageId,
                    IsShared ? "was moved to the dead-letter queue" : "is acknowledged and dropped", ex.Message);
                return FailedSettlement;
            }
        }

        private bool IsShared => options.QueueMode == QueueMode.Shared;

        /// <summary>Shared queues dead-letter a failed message. Per-instance queues ack it anyway, so a bad message can't be redelivered forever.</summary>
        private MessageSettlement FailedSettlement => IsShared ? MessageSettlement.DeadLetter : MessageSettlement.Ack;

        /// <summary>
        /// Our own message coming back: the sender's local side has already seen it. Only on per-instance queues; on a
        /// shared queue another instance's message looks the same as ours.
        /// </summary>
        private bool IsOwnEcho(Dictionary<string, string> headers)
        {
            return !IsShared && headers.TryGetValue(WireHeaders.SourceId, out string sourceId) && sourceId == instanceId;
        }

        private async Task Settle(IChannel channel, ulong deliveryTag, MessageSettlement settlement)
        {
            if (settlement == MessageSettlement.None)
            {
                return;
            }

            try
            {
                if (!channel.IsOpen)
                {
                    return;
                }

                if (settlement == MessageSettlement.DeadLetter)
                {
                    await channel.BasicRejectAsync(deliveryTag, requeue: false).ConfigureAwait(false);
                }
                else
                {
                    await channel.BasicAckAsync(deliveryTag, false).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[{Bus}] Failed to ack message: {Error}", options.BusName, ex.Message);
            }
        }

        #endregion

        #region Publisher

        private async Task OpenPublisher(CancellationToken token)
        {
            IConnection connection = null;
            IChannel channel = null;
            try
            {
                connection = await CreateConnectionFactory()
                    .CreateConnectionAsync(ConnectionName(ConnectionRole.Publisher), token)
                    .ConfigureAwait(false);
                connection.ConnectionShutdownAsync += OnPublisherConnectionShutdown;

                // With confirmation tracking BasicPublishAsync only completes once the broker has the message.
                CreateChannelOptions channelOptions = new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true);
                channel = await connection.CreateChannelAsync(channelOptions, token).ConfigureAwait(false);
                await DeclareExchange(channel, token).ConfigureAwait(false);

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

            if (connection != null)
            {
                connection.ConnectionShutdownAsync -= OnPublisherConnectionShutdown;
            }

            await CloseQuietly(channel, connection).ConfigureAwait(false);
        }

        #endregion

        #region Outstanding queue

        private async Task OutstandingQueue(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                PendingMessage message = null;
                try
                {
                    IChannel channel = publisherChannel;
                    if (channel != null && channel.IsOpen && outstandingMessages.TryPeek(out message))
                    {
                        await PublishMessage(channel, message, token).ConfigureAwait(false);

                        // Only removed once the broker confirmed it; on failure it is retried.
                        outstandingMessages.TryDequeue(out _);
                        continue;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[{Bus}] Publishing {WireName} {MessageId} failed; it stays buffered and is retried: {Error}", options.BusName, message?.WireName, message?.MessageId, ex.Message);
                }

                if (!await Delay(options.OutstandingPollInterval, token).ConfigureAwait(false))
                {
                    break;
                }
            }
        }

        private async Task PublishMessage(IChannel channel, PendingMessage message, CancellationToken token)
        {
            Dictionary<string, object> headers = new Dictionary<string, object>
            {
                [WireHeaders.MessageType] = message.WireName,
                [WireHeaders.SourceId] = instanceId,
            };

            BasicProperties properties = new BasicProperties
            {
                ContentType = MessageSerializer.ContentType,
                MessageId = message.MessageId,
                AppId = options.ClientName,
                Timestamp = new AmqpTimestamp(message.CreatedUtc.ToUnixTimeSeconds()),
                Headers = headers,
            };

            await channel.BasicPublishAsync(
                exchange: options.ExchangeName,
                routingKey: message.RoutingKey,
                mandatory: false,
                basicProperties: properties,
                body: message.Body,
                cancellationToken: token).ConfigureAwait(false);
        }

        #endregion

        #region Helpers

        private ConnectionFactory CreateConnectionFactory()
        {
            ConnectionFactory factory = new ConnectionFactory
            {
                HostName = options.HostName,
                Port = options.Port,
                VirtualHost = options.VirtualHost,
                UserName = options.UserName,
                Password = options.Password,
                RequestedHeartbeat = options.Heartbeat,

                // Reconnecting is done by KeepConnected.
                AutomaticRecoveryEnabled = false,
                TopologyRecoveryEnabled = false,
            };

            // Shown on the connection's page in the management UI. Everyone logs in with the same broker account, so this
            // is what says whose machine a connection is; instance_id is the last part of a per-instance queue's name.
            factory.ClientProperties["app"] = options.ClientName;
            factory.ClientProperties["bus"] = options.BusName;
            factory.ClientProperties["user"] = Environment.UserName;
            factory.ClientProperties["machine"] = Environment.MachineName;
            factory.ClientProperties["process_id"] = ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            factory.ClientProperties["instance_id"] = instanceId;
            return factory;
        }

        /// <summary>
        /// The name in the management UI's connection and channel lists, e.g.
        /// <c>Poc.Net10 [Main] consumer - jsmith</c>. Machine and process are in the client properties.
        /// </summary>
        private string ConnectionName(ConnectionRole role)
        {
            string kind = role == ConnectionRole.Consumer ? "consumer" : "publisher";
            return $"{options.ClientName} [{options.BusName}] {kind} - {Environment.UserName}";
        }

        private static int ProcessId
        {
            get
            {
                using (Process process = Process.GetCurrentProcess())
                {
                    return process.Id;
                }
            }
        }

        /// <summary>The own exchange, as configured. Only the owner declares an exchange (ADR 0002, section 2).</summary>
        private Task DeclareExchange(IChannel channel, CancellationToken token)
        {
            return channel.ExchangeDeclareAsync(
                exchange: options.ExchangeName,
                type: options.ExchangeType,
                durable: options.ExchangeDurable,
                autoDelete: false,
                cancellationToken: token);
        }

        /// <summary>
        /// Checks another application's exchange without declaring it. A missing exchange fails the connect attempt, which
        /// is retried: the owner may simply not have started yet.
        /// </summary>
        private static async Task CheckExchangeExists(IChannel channel, string exchange, CancellationToken token)
        {
            try
            {
                await channel.ExchangeDeclarePassiveAsync(exchange, token).ConfigureAwait(false);
            }
            catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 404)
            {
                throw new InvalidOperationException($"Exchange '{exchange}' does not exist yet; waiting for its owner to declare it.", ex);
            }
        }

        private IEnumerable<string> SubscribedExchanges()
        {
            return options.Subscriptions
                .Select(s => s.Exchange)
                .Where(e => !string.Equals(e, options.ExchangeName, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal);
        }

        /// <summary>
        /// The configured bindings: one per exchange, keyed by the exchange's name, which is the routing key every legacy
        /// application publishes with: the own exchange (unless <see cref="RabbitMQBusOptions.BindOwnExchange"/> is off)
        /// and each subscribed exchange. Routing keys are added by subscribing (<see cref="Bind"/>).
        /// </summary>
        private IEnumerable<(string Exchange, string RoutingKey)> Bindings()
        {
            if (options.BindOwnExchange)
            {
                yield return (options.ExchangeName, options.ExchangeName);
            }

            foreach (SubscriptionOptions subscription in options.Subscriptions)
            {
                yield return (subscription.Exchange, subscription.Exchange);
            }
        }

        private List<(string Exchange, string RoutingKey)> BindingPairs(string exchange, string routingKey)
        {
            if (string.IsNullOrWhiteSpace(routingKey))
            {
                throw new ArgumentException("A routing key is required.", nameof(routingKey));
            }

            IEnumerable<string> exchanges = !string.IsNullOrWhiteSpace(exchange)
                ? new[] { exchange }
                : (options.BindOwnExchange ? new[] { options.ExchangeName } : Array.Empty<string>()).Concat(SubscribedExchanges());

            return exchanges.Select(e => (e, routingKey)).ToList();
        }

        private void ApplyBindingsInBackground()
        {
            if (!started || disposed)
            {
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    CancellationToken token = cancellation.Token;
                    await consumerLock.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        if (IsOpen(consumerConnection, consumerChannel))
                        {
                            await ApplyBindings(consumerConnection, consumerQueueName, token).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        consumerLock.Release();
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
                {
                    // Shutting down.
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[{Bus}] Could not change the routing-key bindings: {Error}", options.BusName, ex.Message);
                }
            });
        }

        /// <summary>
        /// Makes the queue's routing-key bindings match what is requested: binds the new ones and removes those no longer
        /// wanted. Configured bindings are left alone. Call it holding <see cref="consumerLock"/>.
        /// </summary>
        private async Task ApplyBindings(IConnection connection, string queue, CancellationToken token)
        {
            HashSet<(string Exchange, string RoutingKey)> wanted;
            lock (bindingGate)
            {
                wanted = new HashSet<(string Exchange, string RoutingKey)>(requestedBindings.Keys);
            }

            HashSet<(string Exchange, string RoutingKey)> configured = new HashSet<(string Exchange, string RoutingKey)>(Bindings());

            foreach ((string Exchange, string RoutingKey) pair in wanted.Where(p => !configured.Contains(p) && !appliedBindings.Contains(p)).ToList())
            {
                if (await ChangeBinding(connection, queue, pair, bind: true, token).ConfigureAwait(false))
                {
                    appliedBindings.Add(pair);
                }
            }

            foreach ((string Exchange, string RoutingKey) pair in appliedBindings.Where(p => !wanted.Contains(p)).ToList())
            {
                if (await ChangeBinding(connection, queue, pair, bind: false, token).ConfigureAwait(false))
                {
                    appliedBindings.Remove(pair);
                }
            }
        }

        /// <summary>
        /// On a channel of its own: a missing exchange closes the channel it was named on, and that must not be the
        /// consumer's. A failure is logged and tried again at the next change or connect.
        /// </summary>
        private async Task<bool> ChangeBinding(IConnection connection, string queue, (string Exchange, string RoutingKey) pair, bool bind, CancellationToken token)
        {
            IChannel channel = null;
            try
            {
                channel = await connection.CreateChannelAsync(cancellationToken: token).ConfigureAwait(false);
                if (bind)
                {
                    await channel.QueueBindAsync(queue, pair.Exchange, pair.RoutingKey, null, cancellationToken: token).ConfigureAwait(false);
                }
                else
                {
                    await channel.QueueUnbindAsync(queue, pair.Exchange, pair.RoutingKey, null, token).ConfigureAwait(false);
                }

                logger.LogInformation("[{Bus}] {Change} routing key '{RoutingKey}' on exchange '{Exchange}'.",
                    options.BusName, bind ? "Bound" : "Unbound", pair.RoutingKey, pair.Exchange);
                return true;
            }
            catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 404)
            {
                logger.LogWarning("[{Bus}] Exchange '{Exchange}' does not exist yet, so routing key '{RoutingKey}' is not bound. It is tried again at the next connect.",
                    options.BusName, pair.Exchange, pair.RoutingKey);
                return false;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                logger.LogWarning(ex, "[{Bus}] Could not {Change} routing key '{RoutingKey}' on exchange '{Exchange}': {Error}",
                    options.BusName, bind ? "bind" : "unbind", pair.RoutingKey, pair.Exchange, ex.Message);
                return false;
            }
            finally
            {
                await CloseQuietly(channel, null).ConfigureAwait(false);
            }
        }

        private void EnsureNotDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(RabbitMQBus));
            }
        }

        private static bool IsOpen(IConnection connection, IChannel channel)
        {
            return connection != null && connection.IsOpen && channel != null && channel.IsOpen;
        }

        private static Dictionary<string, string> ReadHeaders(IDictionary<string, object> headers)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (headers == null)
            {
                return result;
            }

            foreach (KeyValuePair<string, object> header in headers)
            {
                // String headers arrive as byte[] from the broker.
                result[header.Key] = header.Value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : header.Value?.ToString();
            }

            return result;
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
