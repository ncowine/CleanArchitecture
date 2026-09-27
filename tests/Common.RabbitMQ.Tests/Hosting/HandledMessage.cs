using System;
using System.Diagnostics;
using Messaging;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>What <see cref="CompatMessageHandler"/> saw for one message.</summary>
    public sealed class HandledMessage
    {
        public CompatMessage Message { get; set; }

        public MessageContext Context { get; set; }

        /// <summary>Identifies the DI scope the handler ran in.</summary>
        public Guid ScopeId { get; set; }

        /// <summary><c>CorrelationContext.Current</c> inside the handler.</summary>
        public string CorrelationId { get; set; }

        /// <summary><c>Activity.Current</c> inside the handler.</summary>
        public Activity Activity { get; set; }
    }
}
