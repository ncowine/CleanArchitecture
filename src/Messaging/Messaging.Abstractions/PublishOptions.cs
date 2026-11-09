#nullable enable

namespace Messaging
{
    public sealed class PublishOptions
    {
        public string? Bus { get; set; }

        public string? RoutingKey { get; set; }
    }
}
