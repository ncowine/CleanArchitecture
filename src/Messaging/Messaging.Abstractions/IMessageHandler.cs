using System.Threading;
using System.Threading.Tasks;

namespace Messaging
{
    public interface IMessageHandler<in TMessage>
    {
        Task Handle(TMessage message, MessageContext context, CancellationToken cancellationToken);
    }
}
