using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Messaging;

namespace Common.RabbitMQ.Tests.Broker
{
    /// <summary>Records every callback. Can add headers when publishing, and can throw from every callback.</summary>
    public sealed class RecordingObserver : IMessagingObserver
    {
        private readonly ConcurrentQueue<ObservedEvent> events = new ConcurrentQueue<ObservedEvent>();
        private readonly Action<PublishContext> onPublishing;
        private readonly bool throwing;

        public RecordingObserver(Action<PublishContext> onPublishing = null, bool throwing = false)
        {
            this.onPublishing = onPublishing;
            this.throwing = throwing;
        }

        public IReadOnlyList<ObservedEvent> Events => events.ToList();

        public void OnPublishing(PublishContext context)
        {
            onPublishing?.Invoke(context);
            Record(nameof(OnPublishing), context.WireName, context.MessageId, null, null, null);
        }

        public void OnPublished(PublishContext context)
        {
            Record(nameof(OnPublished), context.WireName, context.MessageId, null, null, null);
        }

        public void OnPublishFailed(PublishContext context, Exception exception)
        {
            Record(nameof(OnPublishFailed), context.WireName, context.MessageId, null, exception, null);
        }

        public void OnReceived(MessageContext context)
        {
            Record(nameof(OnReceived), context.WireName, context.MessageId, context.CorrelationId, null, null);
        }

        public void OnHandled(MessageContext context, TimeSpan duration)
        {
            Record(nameof(OnHandled), context.WireName, context.MessageId, context.CorrelationId, null, null);
        }

        public void OnHandlingFailed(MessageContext context, TimeSpan duration, Exception exception, FailedMessageAction action)
        {
            Record(nameof(OnHandlingFailed), context.WireName, context.MessageId, context.CorrelationId, exception, null, action);
        }

        public void OnConnectionChanged(ConnectionStateChange change)
        {
            Record(nameof(OnConnectionChanged), null, null, null, change.Error, change);
        }

        /// <returns>The first matching event, or null when none is recorded within <paramref name="timeout"/>.</returns>
        public async Task<ObservedEvent> WaitFor(string callback, TimeSpan timeout, Func<ObservedEvent, bool> predicate = null)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (true)
            {
                ObservedEvent match = events.FirstOrDefault(e => e.Callback == callback && (predicate == null || predicate(e)));
                if (match != null || stopwatch.Elapsed > timeout)
                {
                    return match;
                }

                await Task.Delay(20).ConfigureAwait(false);
            }
        }

        public int Count(string callback)
        {
            return events.Count(e => e.Callback == callback);
        }

        private void Record(string callback, string wireName, string messageId, string correlationId, Exception exception, ConnectionStateChange connection, FailedMessageAction? action = null)
        {
            events.Enqueue(new ObservedEvent
            {
                Callback = callback,
                WireName = wireName,
                MessageId = messageId,
                CorrelationId = correlationId,
                Exception = exception,
                Connection = connection,
                Action = action,
                ThreadId = Thread.CurrentThread.ManagedThreadId,
            });

            if (throwing)
            {
                throw new InvalidOperationException($"Observer failure in {callback}.");
            }
        }
    }
}
