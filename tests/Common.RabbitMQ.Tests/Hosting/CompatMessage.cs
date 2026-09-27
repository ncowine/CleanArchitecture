using System;
using Messaging;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>The server-side contract for the legacy <c>Compat.Events.CompatEvent</c>: a plain class, same wire name.</summary>
    [Message("Compat.Events.CompatEvent")]
    public class CompatMessage
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
