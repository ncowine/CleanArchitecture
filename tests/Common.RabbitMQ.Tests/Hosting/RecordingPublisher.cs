using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Messaging;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>An <see cref="IMessagePublisher"/> that only records what it was asked to send.</summary>
    public sealed class RecordingPublisher : IMessagePublisher
    {
        /// <summary>The routing key of each send; null when none was given.</summary>
        public List<string> RoutingKeys { get; } = new List<string>();

        public Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        {
            return PublishAsync(message, null, cancellationToken);
        }

        public Task PublishAsync<TMessage>(TMessage message, PublishOptions options, CancellationToken cancellationToken = default)
        {
            RoutingKeys.Add(options?.RoutingKey);
            return Task.CompletedTask;
        }
    }
}
