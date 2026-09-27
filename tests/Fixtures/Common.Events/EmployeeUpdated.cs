using Common.RabbitMQ;

namespace Common.Events
{
    [RemoteEvent(BusNames.Legacy)]
    public class EmployeeUpdated : RemotePubSubEvent<Employee>
    {
    }
}
