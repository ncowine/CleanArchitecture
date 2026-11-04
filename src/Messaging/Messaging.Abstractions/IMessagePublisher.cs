namespace Messaging
{
    public interface IMessagePublisher
    {
        /// <summary>
        /// Sends <paramref name="message"/> to the default bus. Returns once the message is serialized and buffered;
        /// delivery to the broker happens in the background.
        /// </summary>
        void Publish<TMessage>(TMessage message);

        /// <summary>As <see cref="Publish{TMessage}(TMessage)"/>, with per-call settings such as the bus or the routing key.</summary>
        void Publish<TMessage>(TMessage message, PublishOptions options);
    }
}
