namespace Messaging
{
    public interface IMessagePublisher
    {
        /// <summary>
        /// Sends <paramref name="message"/> to every bus its type is routed to. Returns once the message is
        /// serialized and buffered on each bus; delivery to the broker happens in the background.
        /// </summary>
        void Publish<TMessage>(TMessage message);

        /// <summary>As <see cref="Publish{TMessage}(TMessage)"/>, with per-call settings such as the routing key.</summary>
        void Publish<TMessage>(TMessage message, PublishOptions options);
    }
}
