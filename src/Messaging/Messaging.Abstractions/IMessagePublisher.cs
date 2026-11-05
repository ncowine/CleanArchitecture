namespace Messaging
{
    public interface IMessagePublisher
    {
        /// <summary>
        /// Sends <paramref name="message"/> to the default bus. Returns once the message is serialized and buffered;
        /// delivery to the broker happens in the background.
        /// </summary>
        void Publish<TMessage>(TMessage message);

        void Publish<TMessage>(TMessage message, PublishOptions options);
    }
}
