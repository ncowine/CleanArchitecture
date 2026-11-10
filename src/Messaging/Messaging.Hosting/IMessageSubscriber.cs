using System;
using System.Threading;
using System.Threading.Tasks;

namespace Messaging.Hosting
{
    public interface IMessageSubscriber
    {
        IDisposable Subscribe<TMessage>(Action<TMessage> handler, SynchronizationContext synchronizationContext = null, SubscribeOptions options = null);

        IDisposable Subscribe<TMessage>(Func<TMessage, MessageContext, CancellationToken, Task> handler, SubscribeOptions options = null);
    }
}
