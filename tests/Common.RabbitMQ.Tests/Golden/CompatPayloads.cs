using System;
using Compat.Events;

namespace Common.RabbitMQ.Tests.Golden
{
    public static class CompatPayloads
    {
        /// <summary>The payload whose body is Golden/compat-payload.json.</summary>
        public static CompatPayload Golden()
        {
            return new CompatPayload
            {
                Id = 42,
                Name = "compat",
                UpdatedAt = new DateTime(2026, 9, 26, 10, 15, 30, DateTimeKind.Utc),
            };
        }
    }
}
