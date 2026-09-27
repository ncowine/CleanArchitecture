namespace Messaging
{
    public enum ConnectionStatus
    {
        /// <summary>Connected; for a consumer, its queue is bound and consuming.</summary>
        Connected,

        /// <summary>An open connection dropped. The bus reconnects.</summary>
        Lost,

        /// <summary>A connection attempt failed. The bus retries.</summary>
        Failed,
    }
}
