using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>Singleton the test handler reports to.</summary>
    public sealed class HandledMessages
    {
        private readonly ConcurrentQueue<HandledMessage> messages = new ConcurrentQueue<HandledMessage>();
        private readonly SemaphoreSlim available = new SemaphoreSlim(0);
        private int count;

        public int Count => Volatile.Read(ref count);

        public void Add(HandledMessage message)
        {
            Interlocked.Increment(ref count);
            messages.Enqueue(message);
            available.Release();
        }

        /// <returns>The next handled message, or null when none arrives within <paramref name="timeout"/>.</returns>
        public async Task<HandledMessage> Next(TimeSpan timeout)
        {
            if (!await available.WaitAsync(timeout).ConfigureAwait(false))
            {
                return null;
            }

            messages.TryDequeue(out HandledMessage message);
            return message;
        }
    }
}
