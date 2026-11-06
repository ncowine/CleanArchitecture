using System.Collections.Generic;
using System.Linq;
using Messaging;
using Prism.Events;

namespace Common.RabbitMQ
{
    /// <summary>
    /// The routing keys of one event's subscriptions, by subscription token, so a key is released when its subscription
    /// ends: unsubscribed, or its subscriber collected.
    /// </summary>
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

        /// <summary>Forgets the keys whose subscription is no longer in <paramref name="subscriptions"/>, and returns them.</summary>
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
