using System.Threading;
using System.Threading.Tasks;

namespace Messaging.Hosting
{
    public interface IMessageSink
    {
        Task Deliver(object message, MessageContext context, CancellationToken cancellationToken);
    }
}
