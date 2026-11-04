using Common.RabbitMQ;

namespace Common.Events
{
    /// <summary>Sent by net8 apps when an employee is saved. The API updates its cache.</summary>
    public class EmployeeSaved : RemotePubSubEvent<Employee>
    {
    }
}
