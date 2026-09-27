namespace Messaging
{
    /// <summary>What happens to a message after deserializing or handling it failed.</summary>
    public enum FailedMessageAction
    {
        /// <summary>Acknowledged and gone. Per-instance queues (the legacy behaviour).</summary>
        Dropped,

        /// <summary>The handlers run again after a delay, in the same process.</summary>
        Retrying,

        /// <summary>Moved to the bus's dead-letter queue. Shared queues, once retries are used up.</summary>
        DeadLettered,
    }
}
