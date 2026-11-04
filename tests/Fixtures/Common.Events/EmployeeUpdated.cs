using Common.RabbitMQ;

namespace Common.Events
{
    public class EmployeeUpdated : RemotePubSubEvent<Employee>
    {
    }
}
