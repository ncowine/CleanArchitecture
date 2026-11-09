using System;
using Prism.Events;

namespace Messaging.Prism
{
    internal interface IReceivedEvent
    {
        Type MessageType { get; }

        bool HasSubscribers { get; }

        bool BelongsTo(IEventAggregator eventAggregator);
    }
}
