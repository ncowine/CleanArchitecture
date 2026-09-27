using Common.RabbitMQ;

namespace Common.Events
{
    /// <summary>Sent by net8 apps when an employee is saved. The API updates its cache.</summary>
    [RemoteEvent(BusNames.Modern)]
    public class EmployeeSaved : RemotePubSubEvent<Employee>
    {
    }
}
