using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Common.RabbitMQ.Tests.LegacyModel
{
    /// <summary>Records one Prism event's payloads as they are raised, local or remote.</summary>
    public sealed class Received<TPayload>
        where TPayload : class
    {
        private readonly ConcurrentQueue<TPayload> payloads = new ConcurrentQueue<TPayload>();
        private readonly SemaphoreSlim available = new SemaphoreSlim(0);
        private int count;

        /// <summary>Total received, including those already taken with <see cref="Next"/>.</summary>
        public int Count => Volatile.Read(ref count);

        /// <summary>Runs before a payload is recorded; throw from it to simulate a failing subscriber.</summary>
        public Action<TPayload> OnReceiving { get; set; }

        public void Add(TPayload payload)
        {
            Interlocked.Increment(ref count);
            OnReceiving?.Invoke(payload);
            payloads.Enqueue(payload);
            available.Release();
        }

        /// <returns>The next payload, or null when none arrives within <paramref name="timeout"/>.</returns>
        public async Task<TPayload> Next(TimeSpan timeout)
        {
            if (!await available.WaitAsync(timeout).ConfigureAwait(false))
            {
                return null;
            }

            payloads.TryDequeue(out TPayload payload);
            return payload;
        }
    }
}
