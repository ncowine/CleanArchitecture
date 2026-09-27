using Common.RabbitMQ;

namespace Common.Events
{
    /// <summary>Sent by the API after its employee cache changed, so clients can react.</summary>
    [RemoteEvent(BusNames.Modern)]
    public class EmployeeCacheRefreshed : RemotePubSubEvent<Employee>
    {
    }
}
