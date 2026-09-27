using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Compat.Events;

namespace Common.RabbitMQ.Tests.Broker
{
    public sealed class ReceivedPayloads
    {
        private readonly ConcurrentQueue<CompatPayload> payloads = new ConcurrentQueue<CompatPayload>();
        private readonly SemaphoreSlim available = new SemaphoreSlim(0);
        private int count;

        /// <summary>Total received, including those already taken with <see cref="Next"/>.</summary>
        public int Count => Volatile.Read(ref count);

        public void Add(CompatPayload payload)
        {
            Interlocked.Increment(ref count);
            payloads.Enqueue(payload);
            available.Release();
        }

        /// <returns>The next payload, or null when none arrives within <paramref name="timeout"/>.</returns>
        public async Task<CompatPayload> Next(TimeSpan timeout)
        {
            if (!await available.WaitAsync(timeout).ConfigureAwait(false))
            {
                return null;
            }

            payloads.TryDequeue(out CompatPayload payload);
            return payload;
        }
    }
}
