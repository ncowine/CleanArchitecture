using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Messaging;
using Messaging.Hosting;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>
    /// Reports every message to <see cref="HandledMessages"/>. A message named <see cref="Republish"/> is answered with
    /// one named <see cref="Republished"/>; one named <see cref="Fail"/> throws.
    /// </summary>
    public sealed class CompatMessageHandler : IMessageHandler<CompatMessage>
    {
        public const string Republish = "republish";
        public const string Republished = "republished";
        public const string Fail = "fail";

        private readonly HandledMessages handled;
        private readonly ScopeMarker scope;
        private readonly IMessagePublisher publisher;

        public CompatMessageHandler(HandledMessages handled, ScopeMarker scope, IMessagePublisher publisher)
        {
            this.handled = handled;
            this.scope = scope;
            this.publisher = publisher;
        }

        public async Task Handle(CompatMessage message, MessageContext context, CancellationToken cancellationToken)
        {
            if (message.Name == Fail)
            {
                throw new InvalidOperationException("Handler failed on purpose.");
            }

            handled.Add(new HandledMessage
            {
                Message = message,
                Context = context,
                ScopeId = scope.Id,
                CorrelationId = CorrelationContext.Current,
                Activity = Activity.Current,
            });

            if (message.Name == Republish)
            {
                await publisher.PublishAsync(new CompatMessage { Id = message.Id, Name = Republished, UpdatedAt = message.UpdatedAt }, cancellationToken);
            }
        }
    }
}
