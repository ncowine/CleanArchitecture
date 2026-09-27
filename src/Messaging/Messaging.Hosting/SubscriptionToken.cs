using System;
using System.Threading;

namespace Messaging.Hosting
{
    /// <summary>Returned by <see cref="IMessageSubscriber"/>: disposing it unsubscribes. Disposing twice is harmless.</summary>
    internal sealed class SubscriptionToken : IDisposable
    {
        private Action unsubscribe;

        public SubscriptionToken(Action unsubscribe)
        {
            this.unsubscribe = unsubscribe;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref unsubscribe, null)?.Invoke();
        }
    }
}
