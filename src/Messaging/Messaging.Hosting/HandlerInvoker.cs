using System.Threading;
using System.Threading.Tasks;

namespace Messaging.Hosting
{
    /// <summary>Calls <see cref="IMessageHandler{TMessage}.Handle"/> without reflection on every message.</summary>
    internal static class HandlerInvoker<TMessage>
    {
        public static Task Invoke(object handler, object message, MessageContext context, CancellationToken cancellationToken)
        {
            return ((IMessageHandler<TMessage>)handler).Handle((TMessage)message, context, cancellationToken);
        }
    }
}
