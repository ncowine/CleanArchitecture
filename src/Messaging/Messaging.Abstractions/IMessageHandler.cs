using System.Threading;
using System.Threading.Tasks;

namespace Messaging
{
    /// <summary>Handles one message type. Resolved in its own DI scope for every message.</summary>
    public interface IMessageHandler<in TMessage>
    {
        Task Handle(TMessage message, MessageContext context, CancellationToken cancellationToken);
    }
}
