namespace Common.RabbitMQ
{
    /// <summary>Receiving from another application's exchange (ADR 0002, section 2).</summary>
    public class RabbitMQSubscription
    {
        /// <summary>
        /// The other application's exchange, bound with its name: the routing key its owner publishes with when it sets none.
        /// It is checked, never declared: its owner declares it. Other keys are bound by subscribing with
        /// <see cref="Messaging.SubscribeOptions"/>.
        /// </summary>
        public string Exchange { get; set; }
    }
}
