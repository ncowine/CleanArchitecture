using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Messaging.RabbitMQ
{
    /// <summary>
    /// One connection to one bus (virtual host + exchange).
    /// <para>
    /// <see cref="Start"/> starts three fire-and-forget loops:
    /// <list type="bullet">
    /// <item><see cref="ConnectConsumer"/> - connects, creates this instance's queue, binds the subscribed wire names and
    /// then keeps checking for a dropped connection, reconnecting when needed.</item>
    /// <item><see cref="ConnectPublisher"/> - same, for the publishing connection.</item>
    /// <item><see cref="OutstandingQueue"/> - sends buffered messages whenever the publisher is connected.</item>
    /// </list>
    /// </para>
    /// Every instance has its own exclusive queue, so every instance receives every message; its own messages are
    /// dropped using the <see cref="WireHeaders.SourceId"/> header. Received messages go to the <see cref="IInboundDispatcher"/>.
    /// <para>
    /// The optional <see cref="IMessagingObserver"/> sees every stage and can add headers to outgoing messages.
    /// Without one, nothing beyond the frozen wire format is sent.
    /// </para>
    /// </summary>
    public sealed class RabbitMQBus : IDisposable
    {
        private readonly RabbitMQBusOptions options;
        private readonly IMessageSerializer serializer;
        private readonly IInboundDispatcher dispatcher;
        private readonly IMessagingObserver observer;
        private readonly string instanceId;
        private readonly string queueName;
        private readonly ConcurrentQueue<PendingMessage> outstandingMessages = new ConcurrentQueue<PendingMessage>();
        private readonly ConcurrentDictionary<string, MessageRegistration> subscriptions =
            new ConcurrentDictionary<string, MessageRegistration>(StringComparer.Ordinal);
        private readonly SemaphoreSlim consumerLock = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();

        private IConnection consumerConnection;
        private IChannel consumerChannel;
        private string consumerQueueName;
        private IConnection publisherConnection;
        private IChannel publisherChannel;
        private bool started;
        private bool disposed;

        /// <param name="observer">Optional. Null means no observer and no extra headers.</param>
        public RabbitMQBus(RabbitMQBusOptions options, IMessageSerializer serializer, IInboundDispatcher dispatcher, IMessagingObserver observer = null)
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
            this.serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.observer = observer ?? NullMessagingObserver.Instance;
            instanceId = string.IsNullOrWhiteSpace(options.InstanceId) ? Guid.NewGuid().ToString("N") : options.InstanceId;

            if (options.QueueMode == QueueMode.Shared && (options.MaxAttempts < 1 || options.DeliveryLimit < 1))
            {
                throw new ArgumentException("MaxAttempts and DeliveryLimit must be at least 1.", nameof(options));
            }

            queueName = options.QueueMode == QueueMode.Shared
                ? (string.IsNullOrWhiteSpace(options.SharedQueueName) ? $"{options.ClientName}.{options.BusName}".ToLowerInvariant() : options.SharedQueueName)
                : $"{options.ClientName}.{options.BusName}.{instanceId}".ToLowerInvariant();
        }

        /// <summary>Connection and error messages. The sender is this bus.</summary>
        public event EventHandler<string> Log;

        public string BusName => options.BusName;

        public string InstanceId => instanceId;

        public bool IsConsumerConnected => IsOpen(consumerConnection, consumerChannel);

        public bool IsPublisherConnected => IsOpen(publisherConnection, publisherChannel);

        public int OutstandingCount => outstandingMessages.Count;

        /// <summary>The queue this bus consumes from: its own per-instance queue, or the service's shared queue.</summary>
        public string QueueName => queueName;

        /// <summary>Shared queues only: where failed messages go. Null for per-instance queues.</summary>
        public string DeadLetterQueueName => options.QueueMode == QueueMode.Shared ? queueName + ".dead-letter" : null;

        /// <summary>Subscribes to <paramref name="registrations"/> and starts connecting in the background.</summary>
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

            Task.Run(() => ConnectConsumer(token));
            Task.Run(() => ConnectPublisher(token));
            Task.Run(() => OutstandingQueue(token));
        }

        /// <summary>
        /// Serializes <paramref name="message"/> now and buffers it for publishing as <paramref name="wireName"/>.
        /// Returns immediately; the message is sent once a publisher connection is available.
        /// <see cref="IMessagingObserver.OnPublishing"/> runs here, on the caller's thread.
        /// </summary>
        public void Enqueue(string wireName, object message)
        {
            Enqueue(wireName, message, null);
        }

        /// <summary>
        /// As <see cref="Enqueue(string, object)"/>, routed with <paramref name="routingKey"/> instead of the wire name
        /// (ADR 0002, section 3). The wire name still travels in the event-type header, so receivers identify the message
        /// either way; only subscribers whose bindings match <paramref name="routingKey"/> receive it.
        /// </summary>
        /// <param name="routingKey">Null or empty for the wire name.</param>
        public void Enqueue(string wireName, object message, string routingKey)
        {
            EnsureNotDisposed();

            if (string.IsNullOrWhiteSpace(wireName))
            {
                throw new ArgumentException("A wire name is required.", nameof(wireName));
            }

            byte[] body = serializer.Serialize(message);

            string key = string.IsNullOrEmpty(routingKey) ? wireName : routingKey;
            PublishContext context = new PublishContext(wireName, options.BusName, Guid.NewGuid().ToString("N"), message?.GetType(), key);
            Observe(o => o.OnPublishing(context), nameof(IMessagingObserver.OnPublishing));

            outstandingMessages.Enqueue(new PendingMessage(context, body));
        }

        /// <summary>Starts receiving <paramref name="registration"/>. Does nothing when already subscribed.</summary>
        public async Task Subscribe(MessageRegistration registration)
        {
            EnsureNotDisposed();

            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            if (!subscriptions.TryAdd(registration.WireName, registration))
            {
                return;
            }

            // If not connected the binding is created on the next connect.
            await BindOrUnbind(registration.WireName, bind: true, cancellation.Token).ConfigureAwait(false);
        }

        /// <summary>Stops receiving <paramref name="wireName"/>. Publishing is not affected.</summary>
        public async Task Unsubscribe(string wireName)
        {
            EnsureNotDisposed();

            if (wireName == null || !subscriptions.TryRemove(wireName, out MessageRegistration removed))
            {
                return;
            }

            await BindOrUnbind(removed.WireName, bind: false, cancellation.Token).ConfigureAwait(false);
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
                WriteLog($"Shutting down with {outstandingMessages.Count} unsent message(s).");
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
                        ObserveConnection(ConnectionRole.Consumer, ConnectionStatus.Connected, null, null);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    WriteLog($"Consumer connection failed: {ex.Message}");
                    ObserveConnection(ConnectionRole.Consumer, ConnectionStatus.Failed, null, ex);
                }

                if (!await Delay(options.ReconnectDelay, token).ConfigureAwait(false))
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
                    .CreateConnectionAsync($"{options.ClientName} [{options.BusName}] consumer", token)
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

                IEnumerable<QueueBinding> bindings = subscriptions.Values
                    .SelectMany(r => BindingsFor(r.WireName))
                    .Concat(FixedBindings());
                foreach (QueueBinding binding in bindings)
                {
                    await Bind(channel, queue.QueueName, binding, bind: true, token).ConfigureAwait(false);
                }

                AsyncEventingBasicConsumer consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += OnMessageReceived;
                await channel.BasicConsumeAsync(
                    queue: queue.QueueName,
                    autoAck: false,
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

        /// <summary>One queue per running instance: every instance gets every message, and the queue goes away with it.</summary>
        private Task<QueueDeclareOk> DeclareInstanceQueue(IChannel channel, CancellationToken token)
        {
            return channel.QueueDeclareAsync(
                queue: queueName,
                durable: false,
                exclusive: true,
                autoDelete: true,
                cancellationToken: token);
        }

        /// <summary>
        /// One durable quorum queue for every instance of the service, with its dead-letter queue (ADR 0001, section 6).
        /// Dead-lettering goes through the default exchange straight to the dead-letter queue, at least once.
        /// </summary>
        private async Task<QueueDeclareOk> DeclareSharedQueue(IChannel channel, CancellationToken token)
        {
            await channel.QueueDeclareAsync(
                queue: DeadLetterQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object> { ["x-queue-type"] = "quorum" },
                cancellationToken: token).ConfigureAwait(false);

            return await channel.QueueDeclareAsync(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object>
                {
                    ["x-queue-type"] = "quorum",
                    ["x-delivery-limit"] = options.DeliveryLimit,
                    ["x-dead-letter-exchange"] = "",
                    ["x-dead-letter-routing-key"] = DeadLetterQueueName,

                    // At-least-once dead-lettering requires reject-publish overflow.
                    ["x-dead-letter-strategy"] = "at-least-once",
                    ["x-overflow"] = "reject-publish",
                },
                cancellationToken: token).ConfigureAwait(false);
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

        private async Task BindOrUnbind(string wireName, bool bind, CancellationToken token)
        {
            await consumerLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                IChannel channel = consumerChannel;
                string queueName = consumerQueueName;
                if (channel == null || !channel.IsOpen || queueName == null)
                {
                    return;
                }

                foreach (QueueBinding binding in BindingsFor(wireName))
                {
                    await Bind(channel, queueName, binding, bind, token).ConfigureAwait(false);
                }
            }
            finally
            {
                consumerLock.Release();
            }
        }

        /// <summary>
        /// Per-instance queues (legacy behaviour): drops the bus's own messages, runs the dispatcher once and always
        /// acknowledges. Shared queues: no echo drop (another instance's message is indistinguishable from ours), up to
        /// <see cref="RabbitMQBusOptions.MaxAttempts"/> attempts, then the dead-letter queue. Unknown and undeserializable
        /// messages go straight to the dead-letter queue.
        /// </summary>
        private async Task OnMessageReceived(object sender, BasicDeliverEventArgs args)
        {
            IChannel channel = ((AsyncEventingBasicConsumer)sender).Channel;
            bool shared = options.QueueMode == QueueMode.Shared;
            MessageSettlement settlement = MessageSettlement.Ack;
            MessageContext context = null;
            Stopwatch stopwatch = null;

            try
            {
                CancellationToken token = cancellation.Token;
                Dictionary<string, string> headers = ReadHeaders(args.BasicProperties?.Headers);

                // Our own message coming back - the sender's local side has already seen it.
                if (!shared && headers.TryGetValue(WireHeaders.SourceId, out string sourceId) && sourceId == instanceId)
                {
                    return;
                }

                string wireName = headers.TryGetValue(WireHeaders.MessageType, out string messageType) && messageType != null
                    ? messageType
                    : args.RoutingKey;
                if (!subscriptions.TryGetValue(wireName, out MessageRegistration registration))
                {
                    WriteLog($"Ignored unknown event '{wireName}'.");

                    // With only per-type bindings an unknown message is a mistake worth keeping. Pattern and header
                    // subscriptions bring in unhandled types by design, so those are just skipped.
                    settlement = shared && !HasFixedBindings() ? MessageSettlement.DeadLetter : MessageSettlement.Ack;
                    return;
                }

                headers.TryGetValue(WireHeaders.CorrelationId, out string correlationId);
                context = new MessageContext(wireName, options.BusName, args.BasicProperties?.MessageId, correlationId, args.Redelivered, headers, args.Exchange, args.RoutingKey);
                Observe(o => o.OnReceived(context), nameof(IMessagingObserver.OnReceived));
                stopwatch = Stopwatch.StartNew();

                int attempts = shared ? options.MaxAttempts : 1;
                for (int attempt = 1; ; attempt++)
                {
                    // A fresh copy per attempt, so a handler that changed the message doesn't affect the retry.
                    // A body that can't be deserialized is never retried: it fails the message straight away.
                    object message = serializer.Deserialize(args.Body.ToArray(), registration.MessageType);

                    try
                    {
                        await dispatcher.Dispatch(registration, message, context, args.CancellationToken).ConfigureAwait(false);

                        Observe(o => o.OnHandled(context, stopwatch.Elapsed), nameof(IMessagingObserver.OnHandled));
                        return;
                    }
                    catch (Exception ex) when (attempt < attempts && !token.IsCancellationRequested)
                    {
                        WriteLog($"Failed to handle message '{args.RoutingKey}' (attempt {attempt} of {attempts}), retrying: {ex.Message}");
                        TimeSpan duration = stopwatch.Elapsed;
                        Observe(o => o.OnHandlingFailed(context, duration, ex, FailedMessageAction.Retrying), nameof(IMessagingObserver.OnHandlingFailed));
                    }

                    if (!await Delay(options.RetryDelay, token).ConfigureAwait(false))
                    {
                        settlement = MessageSettlement.None;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                // Per-instance queues: acked anyway so a bad message can't be redelivered forever.
                WriteLog($"Failed to handle message '{args.RoutingKey}': {ex.Message}");
                settlement = shared ? MessageSettlement.DeadLetter : MessageSettlement.Ack;

                if (context != null)
                {
                    TimeSpan duration = stopwatch?.Elapsed ?? TimeSpan.Zero;
                    FailedMessageAction action = shared ? FailedMessageAction.DeadLettered : FailedMessageAction.Dropped;
                    Observe(o => o.OnHandlingFailed(context, duration, ex, action), nameof(IMessagingObserver.OnHandlingFailed));
                }
            }
            finally
            {
                await Settle(channel, args.DeliveryTag, settlement).ConfigureAwait(false);
            }
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
                WriteLog($"Failed to ack message: {ex.Message}");
            }
        }

        private Task OnConsumerConnectionShutdown(object sender, ShutdownEventArgs args)
        {
            WriteLog($"Consumer connection lost: {args.ReplyText}");
            ObserveConnection(ConnectionRole.Consumer, ConnectionStatus.Lost, args.ReplyText, null);
            return Task.CompletedTask;
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
                        ObserveConnection(ConnectionRole.Publisher, ConnectionStatus.Connected, null, null);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    WriteLog($"Publisher connection failed: {ex.Message}");
                    ObserveConnection(ConnectionRole.Publisher, ConnectionStatus.Failed, null, ex);
                }

                if (!await Delay(options.ReconnectDelay, token).ConfigureAwait(false))
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
                    .CreateConnectionAsync($"{options.ClientName} [{options.BusName}] publisher", token)
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

        private Task OnPublisherConnectionShutdown(object sender, ShutdownEventArgs args)
        {
            WriteLog($"Publisher connection lost: {args.ReplyText}");
            ObserveConnection(ConnectionRole.Publisher, ConnectionStatus.Lost, args.ReplyText, null);
            return Task.CompletedTask;
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
                        outstandingMessages.TryDequeue(out PendingMessage published);
                        Observe(o => o.OnPublished(published.Context), nameof(IMessagingObserver.OnPublished));
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

                    if (message != null)
                    {
                        Observe(o => o.OnPublishFailed(message.Context, ex), nameof(IMessagingObserver.OnPublishFailed));
                    }
                }

                if (!await Delay(options.OutstandingPollInterval, token).ConfigureAwait(false))
                {
                    break;
                }
            }
        }

        private async Task PublishMessage(IChannel channel, PendingMessage message, CancellationToken token)
        {
            PublishContext context = message.Context;

            // Frozen headers first and always ours; the observer's headers are additive only.
            Dictionary<string, object> headers = new Dictionary<string, object>
            {
                [WireHeaders.MessageType] = context.WireName,
                [WireHeaders.SourceId] = instanceId,
            };

            foreach (KeyValuePair<string, string> header in context.Headers)
            {
                if (!WireHeaders.IsFrozen(header.Key) && header.Value != null)
                {
                    headers[header.Key] = header.Value;
                }
            }

            BasicProperties properties = new BasicProperties
            {
                ContentType = serializer.ContentType,
                MessageId = context.MessageId,
                AppId = options.ClientName,
                Timestamp = new AmqpTimestamp(message.CreatedUtc.ToUnixTimeSeconds()),
                Headers = headers,
            };

            await channel.BasicPublishAsync(
                exchange: options.ExchangeName,
                routingKey: context.RoutingKey,
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
                HostName = options.HostName,
                Port = options.Port,
                VirtualHost = options.VirtualHost,
                UserName = options.UserName,
                Password = options.Password,
                RequestedHeartbeat = options.Heartbeat,

                // Reconnecting is done by ConnectConsumer / ConnectPublisher.
                AutomaticRecoveryEnabled = false,
                TopologyRecoveryEnabled = false,
            };
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
        /// The bindings a handled message type brings: its wire name on the own exchange (when enabled), and on every
        /// subscription that has neither routing keys nor a header match.
        /// </summary>
        private IEnumerable<QueueBinding> BindingsFor(string wireName)
        {
            if (options.BindOwnExchange)
            {
                yield return new QueueBinding(options.ExchangeName, wireName, null);
            }

            foreach (SubscriptionOptions subscription in options.Subscriptions.Where(IsPerMessageType))
            {
                yield return new QueueBinding(subscription.Exchange, wireName, null);
            }
        }

        /// <summary>The bindings subscriptions ask for regardless of message types: routing keys and header matches.</summary>
        private IEnumerable<QueueBinding> FixedBindings()
        {
            foreach (SubscriptionOptions subscription in options.Subscriptions.Where(s => !IsPerMessageType(s)))
            {
                if (subscription.HeaderMatch.Count > 0)
                {
                    Dictionary<string, object> arguments = new Dictionary<string, object>
                    {
                        ["x-match"] = subscription.MatchAllHeaders ? "all" : "any",
                    };
                    foreach (KeyValuePair<string, string> header in subscription.HeaderMatch)
                    {
                        arguments[header.Key] = header.Value;
                    }

                    yield return new QueueBinding(subscription.Exchange, "", arguments);
                    continue;
                }

                foreach (string routingKey in subscription.RoutingKeys)
                {
                    yield return new QueueBinding(subscription.Exchange, routingKey, null);
                }
            }
        }

        private bool HasFixedBindings()
        {
            return options.Subscriptions.Any(s => !IsPerMessageType(s));
        }

        private static bool IsPerMessageType(SubscriptionOptions subscription)
        {
            return subscription.HeaderMatch.Count == 0 && subscription.RoutingKeys.Count == 0;
        }

        private static Task Bind(IChannel channel, string queueName, QueueBinding binding, bool bind, CancellationToken token)
        {
            return bind
                ? channel.QueueBindAsync(queueName, binding.Exchange, binding.RoutingKey, binding.Arguments, cancellationToken: token)
                : channel.QueueUnbindAsync(queueName, binding.Exchange, binding.RoutingKey, binding.Arguments, cancellationToken: token);
        }

        private void EnsureNotDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(RabbitMQBus));
            }
        }

        private void WriteLog(string message)
        {
            Log?.Invoke(this, message);
        }

        /// <summary>Calls the observer. Its exceptions are logged and never reach the transport.</summary>
        private void Observe(Action<IMessagingObserver> callback, string callbackName)
        {
            try
            {
                callback(observer);
            }
            catch (Exception ex)
            {
                WriteLog($"Observer failed in {callbackName}: {ex.Message}");
            }
        }

        private void ObserveConnection(ConnectionRole role, ConnectionStatus status, string reason, Exception error)
        {
            ConnectionStateChange change = new ConnectionStateChange(options.BusName, role, status, reason, error);
            Observe(o => o.OnConnectionChanged(change), nameof(IMessagingObserver.OnConnectionChanged));
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
