using Common.RabbitMQ;

namespace Common.Events
{
    /// <summary>Sent by the API after its employee cache changed, so clients can react.</summary>
    public class EmployeeCacheRefreshed : RemotePubSubEvent<Employee>
    {
    }
}
