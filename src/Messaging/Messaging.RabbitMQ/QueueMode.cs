namespace Messaging.RabbitMQ
{
    public enum QueueMode
    {
        /// <summary>
        /// One exclusive, auto-delete queue per running instance: every instance receives every message, its own
        /// messages are dropped, and failed messages are acknowledged. The legacy topology (ADR 0001, section 3).
        /// </summary>
        PerInstance,

        /// <summary>
        /// One durable quorum queue shared by every instance of the service: each message goes to one instance.
        /// Failed messages are retried, then dead-lettered (ADR 0001, section 6). For servers only.
        /// </summary>
        Shared,
    }
}
