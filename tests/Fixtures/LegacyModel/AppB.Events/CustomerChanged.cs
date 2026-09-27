using Prism.Events;

namespace AppB.Events
{
    /// <summary>Published by application B to its own exchange.</summary>
    public class CustomerChanged : PubSubEvent<Customer>
    {
    }
}
