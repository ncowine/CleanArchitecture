using System;
using Messaging;

namespace Common.RabbitMQ.Tests.Broker
{
    /// <summary>One observer callback, as <see cref="RecordingObserver"/> saw it.</summary>
    public sealed class ObservedEvent
    {
        public string Callback { get; set; }

        public string WireName { get; set; }

        public string MessageId { get; set; }

        public string CorrelationId { get; set; }

        public Exception Exception { get; set; }

        public ConnectionStateChange Connection { get; set; }

        /// <summary>Set for OnHandlingFailed.</summary>
        public FailedMessageAction? Action { get; set; }

        public int ThreadId { get; set; }
    }
}
