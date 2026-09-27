using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Messaging;
using Messaging.Hosting;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>An <see cref="IMessageSink"/> that records every message it is given.</summary>
    public sealed class RecordingSink : IMessageSink
    {
        private readonly ConcurrentQueue<object> messages = new ConcurrentQueue<object>();
        private readonly SemaphoreSlim available = new SemaphoreSlim(0);

        public Task Deliver(object message, MessageContext context, CancellationToken cancellationToken)
        {
            messages.Enqueue(message);
            available.Release();
            return Task.CompletedTask;
        }

        /// <returns>The next message, or null when none arrives within <paramref name="timeout"/>.</returns>
        public async Task<object> Next(TimeSpan timeout)
        {
            if (!await available.WaitAsync(timeout).ConfigureAwait(false))
            {
                return null;
            }

            messages.TryDequeue(out object message);
            return message;
        }
    }
}
