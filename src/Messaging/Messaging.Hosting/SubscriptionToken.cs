using System;
using System.Threading;

namespace Messaging.Hosting
{
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
