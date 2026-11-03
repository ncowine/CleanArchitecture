using Prism.Events;

namespace Messaging.Prism
{
    /// <summary>
    /// The Prism event for a plain message class. One generic event covers every message, so no <c>PubSubEvent</c> class
    /// is written per message:
    /// <code><![CDATA[
    /// eventAggregator.GetEvent<MessageEvent<EmployeeSaved>>().Subscribe(OnSaved, ThreadOption.UIThread);
    /// eventAggregator.GetEvent<MessageEvent<EmployeeSaved>>().PublishRemote(saved);
    /// ]]></code>
    /// </summary>
    public class MessageEvent<TMessage> : PubSubEvent<TMessage>
    {
    }
}
