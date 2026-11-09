using System;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary>A payload class that hides a tuple in a property. Not a message, so nothing registers it.</summary>
    public class TupleHolder
    {
        public string Name { get; set; }

        public (Guid Id, string Name) Flag { get; set; }
    }
}
