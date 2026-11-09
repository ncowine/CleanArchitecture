using System.Collections.Generic;
using System.Linq;
using Prism.Events;

namespace Messaging.Prism
{
    internal sealed class KeyedSubscriptions
    {
        private readonly Dictionary<SubscriptionToken, SubscribeOptions> keys = new Dictionary<SubscriptionToken, SubscribeOptions>();

        public bool IsEmpty
        {
            get
            {
                lock (keys)
                {
                    return keys.Count == 0;
                }
            }
        }

        public void Add(SubscriptionToken token, SubscribeOptions options)
        {
            lock (keys)
            {
                keys[token] = options;
            }
        }

        public List<SubscribeOptions> RemoveEnded(ICollection<IEventSubscription> subscriptions)
        {
            lock (keys)
            {
                if (keys.Count == 0)
                {
                    return new List<SubscribeOptions>();
                }

                HashSet<SubscriptionToken> live;
                lock (subscriptions)
                {
                    live = new HashSet<SubscriptionToken>(subscriptions.Select(s => s.SubscriptionToken));
                }

                List<KeyValuePair<SubscriptionToken, SubscribeOptions>> ended = keys.Where(k => !live.Contains(k.Key)).ToList();
                foreach (KeyValuePair<SubscriptionToken, SubscribeOptions> entry in ended)
                {
                    keys.Remove(entry.Key);
                }

                return ended.Select(e => e.Value).ToList();
            }
        }
    }
}
