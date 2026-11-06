namespace Messaging
{
    /// <summary>
    /// Receives messages a publisher sends with its own routing key (<see cref="PublishOptions.RoutingKey"/>). Subscribing
    /// with a key binds it on the broker; when the last subscription with that key and exchange goes, the binding is
    /// removed. Several message types can share a key, and so one binding.
    /// <code><![CDATA[
    /// subscriber.Subscribe<EquipmentDisposed>(OnDisposed, new SubscribeOptions { Exchange = "Poc.Net472", RoutingKey = "equipment" });
    /// ]]></code>
    /// Messages sent without a key keep arriving: each exchange in the bus's configured subscriptions is always bound with
    /// its owner's name. A key only adds a binding.
    /// <para>
    /// The binding goes on <see cref="Bus"/> when it is set. Otherwise on the one bus that has <see cref="Exchange"/> as its
    /// own exchange or in its subscriptions, or on the default bus when none has. When several buses have it (the same
    /// exchange name in two vhosts), subscribing throws: set <see cref="Bus"/>.
    /// </para>
    /// </summary>
    public sealed class SubscribeOptions
    {
        /// <summary>The bus to bind the key on. Null to find it from <see cref="Exchange"/>, as described above.</summary>
        public string Bus { get; set; }

        /// <summary>
        /// The exchange to bind the key on: usually another application's, which its owner declares. Null binds it on the
        /// bus's own exchange and on every exchange in its configured subscriptions.
        /// </summary>
        public string Exchange { get; set; }

        /// <summary>The key, or a topic pattern such as <c>equipment.*</c>. Required.</summary>
        public string RoutingKey { get; set; }
    }
}
