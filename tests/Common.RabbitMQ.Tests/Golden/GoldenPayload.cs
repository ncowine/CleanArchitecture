using System;
using System.Collections.Generic;

namespace Common.RabbitMQ.Tests.Golden
{
    /// <summary>Covers the value types whose JSON form differs between serializers.</summary>
    public class GoldenPayload
    {
        public decimal Amount { get; set; }

        public decimal WholeAmount { get; set; }

        public double Ratio { get; set; }

        public double WholeRatio { get; set; }

        public GoldenColor Color { get; set; }

        public Guid Key { get; set; }

        public DateTimeOffset At { get; set; }

        public int? Missing { get; set; }

        public List<string> Tags { get; set; }

        public byte[] Data { get; set; }

        public bool Flag { get; set; }

        public long Big { get; set; }
    }
}
