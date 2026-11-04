namespace Common.RabbitMQ
{
    /// <summary>What <see cref="RemoteEventSubscriptions"/> needs from a <see cref="RemotePubSubEvent{TPayload}"/>, whatever its payload.</summary>
    internal interface IRemoteEvent
    {
        /// <summary>False once the last subscriber has unsubscribed or been garbage collected.</summary>
        bool HasSubscribers { get; }
    }
}
