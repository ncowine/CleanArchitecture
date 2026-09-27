using Prism.Events;

namespace AppA.Events
{
    /// <summary>Published by application A to its own exchange.</summary>
    public class OrderSaved : PubSubEvent<Order>
    {
    }
}
