using System.Threading;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>Stands in for a UI thread's context: counts posts and runs each one on a thread-pool thread.</summary>
    public sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        private int posts;

        public int Posts => Volatile.Read(ref posts);

        public override void Post(SendOrPostCallback callback, object state)
        {
            Interlocked.Increment(ref posts);
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }
    }
}
