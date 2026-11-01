using System.Collections.Generic;
using Messaging;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>An <see cref="IMessagePublisher"/> that only records what it was asked to send.</summary>
    public sealed class RecordingPublisher : IMessagePublisher
    {
        /// <summary>The routing key of each send; null when none was given.</summary>
        public List<string> RoutingKeys { get; } = new List<string>();

        public void Publish<TMessage>(TMessage message)
        {
            Publish(message, null);
        }

        public void Publish<TMessage>(TMessage message, PublishOptions options)
        {
            RoutingKeys.Add(options?.RoutingKey);
        }
    }
}
