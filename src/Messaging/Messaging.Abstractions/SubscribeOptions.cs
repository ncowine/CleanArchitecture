#nullable enable

namespace Messaging
{
    public sealed class SubscribeOptions
    {
        public string? Bus { get; set; }

        public string? Exchange { get; set; }

        public string RoutingKey { get; set; } = null!;
    }
}
