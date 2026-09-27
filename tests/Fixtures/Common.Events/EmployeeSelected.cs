using Prism.Events;

namespace Common.Events
{
    /// <summary>Local event: raised when an employee is selected. Never leaves the application.</summary>
    public class EmployeeSelected : PubSubEvent<Employee>
    {
    }
}
