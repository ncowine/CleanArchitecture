using System;
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
    }
}
