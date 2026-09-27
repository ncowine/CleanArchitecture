using System.Threading;
using System.Threading.Tasks;

namespace Messaging.Hosting
{
    /// <summary>
    /// Receives every message the application takes in, whatever its type, after the type's handlers. Registered as a
    /// DI service (singleton or scoped) and resolved in the message's scope. The event aggregator bridge
    /// (Messaging.Prism) is one.
    /// </summary>
    public interface IMessageSink
    {
        Task Deliver(object message, MessageContext context, CancellationToken cancellationToken);
    }
}
