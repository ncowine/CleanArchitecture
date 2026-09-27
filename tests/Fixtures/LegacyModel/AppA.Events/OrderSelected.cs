using Prism.Events;

namespace AppA.Events
{
    /// <summary>Local only: raised with Publish, never PublishRemote. Registered anyway, harmlessly.</summary>
    public class OrderSelected : PubSubEvent<Order>
    {
    }
}
