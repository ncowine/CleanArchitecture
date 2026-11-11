using System.Threading;
using System.Threading.Tasks;

namespace Messaging.Hosting
{
    internal interface IMessageSink
    {
        Task DeliverAsync(object message, MessageContext context, CancellationToken cancellationToken);
    }
}
