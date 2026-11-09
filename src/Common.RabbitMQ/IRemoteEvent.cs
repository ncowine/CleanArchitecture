namespace Common.RabbitMQ
{
    internal interface IRemoteEvent
    {
        bool HasSubscribers { get; }
    }
}
