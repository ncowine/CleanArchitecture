namespace Messaging.RabbitMQ
{
    /// <summary>How a delivery is finished.</summary>
    internal enum MessageSettlement
    {
        Ack,

        /// <summary>Rejected without requeue: the broker moves it to the dead-letter queue.</summary>
        DeadLetter,

        /// <summary>Left unsettled while shutting down: the broker redelivers it once the channel closes.</summary>
        None,
    }
}
