using Prism.Events;

namespace Messaging.Prism
{
    /// <summary>
    /// The Prism event for a plain message class. One generic event covers every message, so no <c>PubSubEvent</c> class
    /// is written per message:
    /// <code>
    /// eventAggregator.GetEvent&lt;MessageEvent&lt;EmployeeSaved&gt;&gt;().Subscribe(OnSaved, ThreadOption.UIThread);
    /// eventAggregator.GetEvent&lt;MessageEvent&lt;EmployeeSaved&gt;&gt;().PublishRemote(saved);
    /// </code>
    /// </summary>
    public class MessageEvent<TMessage> : PubSubEvent<TMessage>
    {
    }
}
