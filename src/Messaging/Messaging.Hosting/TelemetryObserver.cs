using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Messaging.RabbitMQ;
using Microsoft.Extensions.Logging;

namespace Messaging.Hosting
{
    /// <summary>
    /// Traces, metrics and logs for every bus, following the OpenTelemetry messaging semantic conventions
    /// (ADR 0001, section 5). Adds the correlation-id, traceparent and tracestate headers to outgoing messages and
    /// continues the sender's trace on receive. Server only: legacy clients never load it.
    /// </summary>
    internal sealed class TelemetryObserver : IMessagingObserver
    {
        private const string ActivityItem = "Messaging.Hosting.Activity";

        private static readonly ActivitySource source = new ActivitySource(MessagingTelemetry.Name);
        private static readonly Meter meter = new Meter(MessagingTelemetry.Name);
        private static readonly Counter<long> sentMessages = meter.CreateCounter<long>(MessagingTelemetry.SentMessages, "{message}");
        private static readonly Counter<long> consumedMessages = meter.CreateCounter<long>(MessagingTelemetry.ConsumedMessages, "{message}");
        private static readonly Histogram<double> processDuration = meter.CreateHistogram<double>(MessagingTelemetry.ProcessDuration, "s");

        private readonly ILogger<TelemetryObserver> logger;

        public TelemetryObserver(ILogger<TelemetryObserver> logger)
        {
            this.logger = logger;
        }

        public void OnPublishing(PublishContext context)
        {
            // A message published outside any operation starts its own conversation.
            string correlationId = CorrelationContext.Current ?? context.MessageId;
            context.Headers[WireHeaders.CorrelationId] = correlationId;

            Activity caller = Activity.Current;
            Activity activity = source.StartActivity($"publish {context.WireName}", ActivityKind.Producer);

            // Without a listener there is no span of our own, but the caller's trace still travels with the message.
            Activity propagated = activity ?? caller;
            if (propagated != null && propagated.IdFormat == ActivityIdFormat.W3C)
            {
                context.Headers[WireHeaders.TraceParent] = propagated.Id;
                if (!string.IsNullOrEmpty(propagated.TraceStateString))
                {
                    context.Headers[WireHeaders.TraceState] = propagated.TraceStateString;
                }
            }

            if (activity != null)
            {
                SetTags(activity, "publish", "send", context.BusName, context.WireName, context.MessageId, correlationId);
                activity.SetTag("messaging.rabbitmq.destination.routing_key", context.RoutingKey);
                context.Items[ActivityItem] = activity;

                // The span stays open until the broker confirms; it must not become the caller's current activity.
                Activity.Current = caller;
            }
        }

        public void OnPublished(PublishContext context)
        {
            sentMessages.Add(1, Tags(context.BusName, context.WireName, null));
            StopOffThread(context.Items);
        }

        public void OnPublishFailed(PublishContext context, Exception exception)
        {
            sentMessages.Add(1, Tags(context.BusName, context.WireName, exception.GetType().FullName));
            logger.LogWarning(exception, "[{Bus}] Publishing {WireName} {MessageId} failed; it stays buffered and is retried.", context.BusName, context.WireName, context.MessageId);

            if (context.Items.TryGetValue(ActivityItem, out object item))
            {
                AddException((Activity)item, exception);
            }
        }

        public void OnReceived(MessageContext context)
        {
            ActivityContext parent = default;
            if (context.Headers.TryGetValue(WireHeaders.TraceParent, out string traceParent) && traceParent != null)
            {
                context.Headers.TryGetValue(WireHeaders.TraceState, out string traceState);
                ActivityContext.TryParse(traceParent, traceState, isRemote: true, out parent);
            }

            consumedMessages.Add(1, Tags(context.BusName, context.WireName, null));

            // Becomes the current activity for the handlers: the core dispatches right after this call.
            Activity activity = source.StartActivity($"process {context.WireName}", ActivityKind.Consumer, parent);
            if (activity != null)
            {
                SetTags(activity, "process", "process", context.BusName, context.WireName, context.MessageId, context.CorrelationId);
                activity.SetTag("messaging.rabbitmq.destination.routing_key", context.RoutingKey);
                context.Items[ActivityItem] = activity;
            }
        }

        public void OnHandled(MessageContext context, TimeSpan duration)
        {
            processDuration.Record(duration.TotalSeconds, Tags(context.BusName, context.WireName, null));

            if (context.Items.TryGetValue(ActivityItem, out object item))
            {
                ((Activity)item).Stop();
            }
        }

        public void OnHandlingFailed(MessageContext context, TimeSpan duration, Exception exception, FailedMessageAction action)
        {
            Activity activity = context.Items.TryGetValue(ActivityItem, out object item) ? (Activity)item : null;
            if (activity != null)
            {
                AddException(activity, exception);
            }

            // A retry is an event on the same span; the span and the duration end with the final outcome.
            if (action == FailedMessageAction.Retrying)
            {
                logger.LogWarning(exception, "[{Bus}] Handling {WireName} {MessageId} failed; retrying.", context.BusName, context.WireName, context.MessageId);
                return;
            }

            processDuration.Record(duration.TotalSeconds, Tags(context.BusName, context.WireName, exception.GetType().FullName));
            if (action == FailedMessageAction.DeadLettered)
            {
                logger.LogError(exception, "[{Bus}] Handling {WireName} {MessageId} failed; the message was moved to the dead-letter queue.", context.BusName, context.WireName, context.MessageId);
            }
            else
            {
                logger.LogError(exception, "[{Bus}] Handling {WireName} {MessageId} failed; the message is acknowledged and dropped.", context.BusName, context.WireName, context.MessageId);
            }

            if (activity != null)
            {
                activity.SetTag("messaging.failure.action", action.ToString());
                activity.SetStatus(ActivityStatusCode.Error, exception.Message);
                activity.Stop();
            }
        }

        public void OnConnectionChanged(ConnectionStateChange change)
        {
            switch (change.Status)
            {
                case ConnectionStatus.Connected:
                    logger.LogInformation("[{Bus}] {Role} connected.", change.BusName, change.Role);
                    break;
                case ConnectionStatus.Lost:
                    logger.LogWarning("[{Bus}] {Role} connection lost: {Reason}. Reconnecting.", change.BusName, change.Role, change.Reason);
                    break;
                default:
                    logger.LogWarning(change.Error, "[{Bus}] {Role} could not connect. Retrying.", change.BusName, change.Role);
                    break;
            }
        }

        /// <summary>
        /// Stops a publish span on the bus's background loop without leaving it, or its parent, as that loop's
        /// current activity.
        /// </summary>
        private static void StopOffThread(IDictionary<string, object> items)
        {
            if (!items.TryGetValue(ActivityItem, out object item))
            {
                return;
            }

            Activity current = Activity.Current;
            ((Activity)item).Stop();
            Activity.Current = current;
        }

        private static void SetTags(Activity activity, string operationName, string operationType, string busName, string wireName, string messageId, string correlationId)
        {
            activity.SetTag("messaging.system", "rabbitmq");
            activity.SetTag("messaging.operation.name", operationName);
            activity.SetTag("messaging.operation.type", operationType);
            activity.SetTag("messaging.destination.name", wireName);
            activity.SetTag("messaging.message.id", messageId);
            activity.SetTag("messaging.message.conversation_id", correlationId);
            activity.SetTag("messaging.bus.name", busName);
        }

        private static void AddException(Activity activity, Exception exception)
        {
            activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                ["exception.type"] = exception.GetType().FullName,
                ["exception.message"] = exception.Message,
                ["exception.stacktrace"] = exception.ToString(),
            }));
        }

        private static TagList Tags(string busName, string wireName, string errorType)
        {
            TagList tags = new TagList
            {
                { "messaging.system", "rabbitmq" },
                { "messaging.destination.name", wireName },
                { "messaging.bus.name", busName },
            };

            if (errorType != null)
            {
                tags.Add("error.type", errorType);
            }

            return tags;
        }
    }
}
