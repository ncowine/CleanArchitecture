using System;
using Prism.Events;

namespace Messaging.Prism
{
    /// <summary>What <see cref="MessageEventReceiver"/> needs from a <see cref="MessageEvent{TMessage}"/>, whatever its message.</summary>
    internal interface IReceivedEvent
    {
        Type MessageType { get; }

        /// <summary>False once the last subscriber has unsubscribed or been garbage collected.</summary>
        bool HasSubscribers { get; }

        bool BelongsTo(IEventAggregator eventAggregator);
    }
}
