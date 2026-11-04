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
    /// <para>
    /// The optional <see cref="IMessagingObserver"/> sees every stage and can add headers to outgoing messages.
    /// Without one, nothing beyond the frozen wire format is sent.
    /// </para>
    /// </summary>
    public sealed class RabbitMQBus : IDisposable
    {
        /// <summary>How long <see cref="PublishConfirmedAsync"/> waits for the broker's confirm before giving up.</summary>
        private static readonly TimeSpan ConfirmTimeout = TimeSpan.FromSeconds(30);

        private readonly RabbitMQBusOptions options;
        private readonly IMessageSerializer serializer;
        private readonly IInboundDispatcher dispatcher;
        private readonly IMessagingObserver observer;
        private readonly string instanceId;
        private readonly string queueName;
        private readonly ILogger logger;
        private readonly ConcurrentQueue<PendingMessage> outstandingMessages = new ConcurrentQueue<PendingMessage>();
        private readonly ConcurrentDictionary<string, MessageRegistration> subscriptions =
            new ConcurrentDictionary<string, MessageRegistration>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, bool> released = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
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
        /// <param name="logger">Optional. Null means no logging.</param>
        public RabbitMQBus(RabbitMQBusOptions options, IMessageSerializer serializer, IInboundDispatcher dispatcher, IMessagingObserver observer = null, ILogger logger = null)
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
            this.logger = logger ?? NullLogger.Instance;
            instanceId = string.IsNullOrWhiteSpace(options.InstanceId) ? Guid.NewGuid().ToString("N") : options.InstanceId;

            if (options.QueueMode == QueueMode.Shared && (options.MaxAttempts < 1 || options.DeliveryLimit < 1))
            {
                throw new ArgumentException("MaxAttempts and DeliveryLimit must be at least 1.", nameof(options));
            }

            queueName = options.QueueMode == QueueMode.Shared
                ? (string.IsNullOrWhiteSpace(options.SharedQueueName) ? $"{options.ClientName}.{options.BusName}".ToLowerInvariant() : options.SharedQueueName)
                : $"{options.ClientName}.{options.BusName}.{instanceId}".ToLowerInvariant();
        }

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

            Task.Run(() => KeepConnected(ConnectionRole.Consumer, token));
            Task.Run(() => KeepConnected(ConnectionRole.Publisher, token));
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
        /// As <see cref="Enqueue(string, object)"/>, routed with <paramref name="routingKey"/> instead of the wire name. The
        /// wire name still travels in the event-type header, so receivers identify the message either way.
        /// </summary>
        /// <param name="routingKey">Null or empty for the wire name.</param>
        public void Enqueue(string wireName, object message, string routingKey)
        {
            EnsureNotDisposed();

            outstandingMessages.Enqueue(Prepare(wireName, message, routingKey, messageId: null, retriedOnFailure: true));
        }

        /// <summary>
        /// Publishes <paramref name="message"/> now and completes once the broker has confirmed it (ADR 0003). Nothing is
        /// buffered: the caller keeps the message until this succeeds, which is what an outbox needs. A message no queue
        /// is bound for still completes (normal while no receiver runs), but it is logged.
        /// </summary>
        /// <param name="routingKey">Null or empty for the wire name.</param>
        /// <param name="messageId">Null for a new one. An outbox passes its row's ID, so a message sent again keeps its ID.</param>
        /// <exception cref="BrokerUnavailableException">
        /// The bus isn't connected or is stopped, the connection dropped, the broker refused the message (nack), or no
        /// confirm came within <see cref="ConfirmTimeout"/>. Not the message's fault: try again later.
        /// </exception>
        public async Task PublishConfirmedAsync(string wireName, object message, string routingKey, string messageId, CancellationToken cancellationToken)
        {
            // Not ObjectDisposedException: to a caller such as an outbox, a stopped bus is just one that can't publish now.
            if (disposed)
            {
                throw new BrokerUnavailableException($"Bus '{options.BusName}' has been stopped.");
            }

            PendingMessage pending = Prepare(wireName, message, routingKey, messageId, retriedOnFailure: false);
            PublishContext context = pending.Context;

            // RabbitMQ.Client 7 serializes publishes on a channel itself and waits for each confirm separately, so this
            // shares the publisher channel with the outgoing buffer's loop safely.
            IChannel channel = publisherChannel;
            if (channel == null || !channel.IsOpen)
            {
                BrokerUnavailableException unavailable = new BrokerUnavailableException($"Bus '{options.BusName}' is not connected to the broker.");
                Observe(o => o.OnPublishFailed(context, unavailable), nameof(IMessagingObserver.OnPublishFailed));
                throw unavailable;
            }

            // A connection the broker has blocked (memory or disk alarm), or a half-open one, never confirms. Without a limit
            // the caller would wait until shutdown, holding up everything queued behind this message.
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(ConfirmTimeout);

                try
                {
                    await PublishMessage(channel, pending, mandatory: true, timeout.Token).ConfigureAwait(false);
                }
                catch (PublishException ex) when (ex.IsReturn)
                {
                    logger.LogWarning("[{Bus}] No queue is bound to exchange '{Exchange}' for routing key '{RoutingKey}': message {MessageId} ({WireName}) reached no receiver.", options.BusName, options.ExchangeName, context.RoutingKey, context.MessageId, wireName);
                }
                catch (Exception ex)
                {
                    bool timedOut = timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
                    BrokerUnavailableException unavailable = AsBrokerUnavailable(ex, channel, timedOut);

                    // The observer hears about every failure, so the publish's span always ends. A timeout is reported as
                    // the timeout, anything else as the error the client raised.
                    Exception reported = timedOut ? unavailable : ex;
                    Observe(o => o.OnPublishFailed(context, reported), nameof(IMessagingObserver.OnPublishFailed));

                    if (unavailable == null)
                    {
                        throw;
                    }

                    throw unavailable;
                }
            }

            Observe(o => o.OnPublished(context), nameof(IMessagingObserver.OnPublished));
        }

        /// <summary>
        /// The <see cref="BrokerUnavailableException"/> a failed confirmed publish becomes, or null to rethrow the original:
        /// when the caller cancelled, or the error isn't the broker's.
        /// </summary>
        private BrokerUnavailableException AsBrokerUnavailable(Exception ex, IChannel channel, bool timedOut)
        {
            if (timedOut)
            {
                return new BrokerUnavailableException($"Bus '{options.BusName}' got no confirm from the broker within {ConfirmTimeout.TotalSeconds:0} s.", ex);
            }

            // The caller gave up, e.g. the host is shutting down. The message may or may not be on the broker.
            if (ex is OperationCanceledException)
            {
                return null;
            }

            // A nack: the broker refused the message, e.g. a full queue that rejects publishes. Like a lost connection,
            // it's the broker's state, not the message, so it's worth a retry.
            if (ex is PublishException)
            {
                return new BrokerUnavailableException($"Bus '{options.BusName}': the broker refused the message (nack).", ex);
            }

            if (ex is OperationInterruptedException || ex is ObjectDisposedException || !channel.IsOpen)
            {
                return new BrokerUnavailableException($"Bus '{options.BusName}' lost its connection while publishing.", ex);
            }

            return null;
        }

        /// <summary>The steps both publish paths share: serialize, pick the routing key and ID, tell the observer.</summary>
        private PendingMessage Prepare(string wireName, object message, string routingKey, string messageId, bool retriedOnFailure)
        {
            if (string.IsNullOrWhiteSpace(wireName))
            {
                throw new ArgumentException("A wire name is required.", nameof(wireName));
            }

            byte[] body = serializer.Serialize(message);

            string key = string.IsNullOrEmpty(routingKey) ? wireName : routingKey;
            string id = string.IsNullOrEmpty(messageId) ? Guid.NewGuid().ToString("N") : messageId;
            PublishContext context = new PublishContext(wireName, options.BusName, id, message?.GetType(), key, retriedOnFailure);
            Observe(o => o.OnPublishing(context), nameof(IMessagingObserver.OnPublishing));

            return new PendingMessage(context, body);
        }

        /// <summary>
        /// Starts receiving <paramref name="registration"/>. Does nothing when already subscribed. The bus knows the message
        /// type before this returns; the binding is created on a background thread, or on the next connect when not connected.
        /// </summary>
        public async Task Subscribe(MessageRegistration registration)
        {
            EnsureNotDisposed();

            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            released.TryRemove(registration.WireName, out _);

            if (!subscriptions.TryAdd(registration.WireName, registration))
            {
                return;
            }

            if (started && IsShared)
            {
                logger.LogWarning("[{Bus}] '{WireName}' was subscribed after the bus started. Messages for it that reached this durable queue earlier may have been dead-lettered or dropped: subscribe before starting.",
                    options.BusName, registration.WireName);
            }

            await ApplyBindingInBackground(registration.WireName).ConfigureAwait(false);
        }

        /// <summary>Stops receiving <paramref name="wireName"/>, for every instance on a shared queue. Publishing is not affected.</summary>
        public async Task Unsubscribe(string wireName)
        {
            EnsureNotDisposed();

            if (wireName == null || !subscriptions.TryRemove(wireName, out _))
            {
                return;
            }

            await ApplyBindingInBackground(wireName).ConfigureAwait(false);
        }

        /// <summary>
        /// This application has no more subscribers for <paramref name="wireName"/>. A per-instance queue is unbound. A shared
        /// queue's binding belongs to every instance of the service once running, so it stays, and this instance acknowledges
        /// and drops the messages the broker gives it, with a warning at the first.
        /// </summary>
        public Task ReleaseSubscription(string wireName)
        {
            if (IsShared && started)
            {
                released[wireName] = true;
                return Task.CompletedTask;
            }

            return Unsubscribe(wireName);
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

        /// <summary>
        /// Runs until the bus stops. Every <see cref="RabbitMQBusOptions.ReconnectDelay"/> it checks the connection for
        /// <paramref name="role"/> and, when it's missing or dropped, closes what's left and opens a new one.
        /// </summary>
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
                        ObserveConnection(role, ConnectionStatus.Connected, null, null);
                    }
                    else if (role == ConnectionRole.Publisher && !IsPublisherConnected)
                    {
                        await ClosePublisher().ConfigureAwait(false);
                        await OpenPublisher(token).ConfigureAwait(false);
                        logger.LogInformation("[{Bus}] Publisher connected.", options.BusName);
                        ObserveConnection(role, ConnectionStatus.Connected, null, null);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[{Bus}] {Role} could not connect: {Error}. Retrying.", options.BusName, role, ex.Message);
                    ObserveConnection(role, ConnectionStatus.Failed, null, ex);
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
            ObserveConnection(role, ConnectionStatus.Lost, args.ReplyText, null);
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

        /// <summary>Off the caller's thread, usually the UI thread, because this talks to the broker.</summary>
        private Task ApplyBindingInBackground(string wireName)
        {
            CancellationToken token = cancellation.Token;
            return Task.Run(() => ApplyBinding(wireName, token));
        }

        /// <summary>
        /// Makes the broker match <see cref="subscriptions"/> as it is once the lock is held, so a quick unsubscribe and
        /// subscribe end in the right state whichever finishes first.
        /// </summary>
        private async Task ApplyBinding(string wireName, CancellationToken token)
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

                bool bind = subscriptions.ContainsKey(wireName);
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

        /// <summary>RabbitMQ.Client calls this for every delivery: <see cref="Receive"/> decides, then the delivery is settled.</summary>
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

        /// <summary>Identifies one delivery and hands it to the dispatcher. Returns what to tell the broker.</summary>
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
                logger.LogInformation("[{Bus}] Ignored unknown event '{WireName}'.", options.BusName, wireName);

                // With only per-type bindings an unknown message is a mistake worth keeping. Pattern and header
                // subscriptions bring in unhandled types by design, so those are just skipped.
                return IsShared && !HasFixedBindings() ? MessageSettlement.DeadLetter : MessageSettlement.Ack;
            }

            if (released.TryRemove(wireName, out _))
            {
                logger.LogWarning("[{Bus}] '{WireName}' has no subscribers left on this instance, but its shared queue stays bound for the other instances. Messages the broker gives this instance are dropped.",
                    options.BusName, wireName);
            }

            headers.TryGetValue(WireHeaders.CorrelationId, out string correlationId);
            MessageContext context = new MessageContext(wireName, options.BusName, args.BasicProperties?.MessageId, correlationId, args.Redelivered, headers, args.Exchange, args.RoutingKey);
            Observe(o => o.OnReceived(context), nameof(IMessagingObserver.OnReceived));

            return await HandleWithRetries(registration, context, args, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Runs the dispatcher: once on a per-instance queue, up to <see cref="RabbitMQBusOptions.MaxAttempts"/> times on a
        /// shared one, waiting <see cref="RabbitMQBusOptions.RetryDelay"/> between attempts.
        /// </summary>
        private async Task<MessageSettlement> HandleWithRetries(MessageRegistration registration, MessageContext context, BasicDeliverEventArgs args, CancellationToken token)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            int attempts = IsShared ? options.MaxAttempts : 1;

            try
            {
                for (int attempt = 1; ; attempt++)
                {
                    // A fresh copy per attempt, so a handler that changed the message doesn't affect the retry.
                    // A body that can't be deserialized is never retried: it fails the message straight away.
                    object message = serializer.Deserialize(args.Body.ToArray(), registration.MessageType);

                    try
                    {
                        await dispatcher.Dispatch(registration, message, context, args.CancellationToken).ConfigureAwait(false);

                        Observe(o => o.OnHandled(context, stopwatch.Elapsed), nameof(IMessagingObserver.OnHandled));
                        return MessageSettlement.Ack;
                    }
                    catch (Exception ex) when (attempt < attempts && !token.IsCancellationRequested)
                    {
                        logger.LogWarning(ex, "[{Bus}] Handling {WireName} {MessageId} (attempt {Attempt} of {Attempts}) failed; retrying: {Error}", options.BusName, context.WireName, context.MessageId, attempt, attempts, ex.Message);
                        TimeSpan duration = stopwatch.Elapsed;
                        Observe(o => o.OnHandlingFailed(context, duration, ex, FailedMessageAction.Retrying), nameof(IMessagingObserver.OnHandlingFailed));
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
                TimeSpan duration = stopwatch.Elapsed;
                FailedMessageAction action = IsShared ? FailedMessageAction.DeadLettered : FailedMessageAction.Dropped;
                logger.LogError(ex, "[{Bus}] Handling {WireName} {MessageId} failed; the message {Outcome}: {Error}", options.BusName, context.WireName, context.MessageId,
                    action == FailedMessageAction.DeadLettered ? "was moved to the dead-letter queue" : "is acknowledged and dropped", ex.Message);
                Observe(o => o.OnHandlingFailed(context, duration, ex, action), nameof(IMessagingObserver.OnHandlingFailed));
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
                        await PublishMessage(channel, message, mandatory: false, token).ConfigureAwait(false);

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
                    logger.LogWarning(ex, "[{Bus}] Publishing {WireName} {MessageId} failed; it stays buffered and is retried: {Error}", options.BusName, message?.Context.WireName, message?.Context.MessageId, ex.Message);

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

        /// <param name="mandatory">
        /// False on the buffered path, as legacy apps always published. With true, a message no queue is bound for comes
        /// back as a <see cref="PublishException"/> whose <see cref="PublishException.IsReturn"/> is set.
        /// </param>
        private async Task PublishMessage(IChannel channel, PendingMessage message, bool mandatory, CancellationToken token)
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
                mandatory: mandatory,
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

                // Reconnecting is done by KeepConnected.
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

        /// <summary>Calls the observer. Its exceptions are logged and never reach the transport.</summary>
        private void Observe(Action<IMessagingObserver> callback, string callbackName)
        {
            try
            {
                callback(observer);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{Bus}] Observer failed in {Callback}: {Error}", options.BusName, callbackName, ex.Message);
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
