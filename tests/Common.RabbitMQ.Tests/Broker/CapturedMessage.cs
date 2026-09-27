using System.Collections.Generic;

namespace Common.RabbitMQ.Tests.Broker
{
    /// <summary>A message as it crossed the wire, copied out of the delivery.</summary>
    public sealed class CapturedMessage
    {
        public string Exchange { get; set; }

        public string RoutingKey { get; set; }

        public string ContentType { get; set; }

        public string MessageId { get; set; }

        public string AppId { get; set; }

        /// <summary>Header values decoded as UTF-8 strings.</summary>
        public Dictionary<string, string> Headers { get; set; }

        public byte[] Body { get; set; }
    }
}
