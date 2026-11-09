namespace Messaging
{
    public interface IMessagePublisher
    {
        void Publish<TMessage>(TMessage message);

        void Publish<TMessage>(TMessage message, PublishOptions options);
    }
}
