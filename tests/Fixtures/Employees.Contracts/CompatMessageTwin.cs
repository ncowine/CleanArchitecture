using Messaging;

namespace Employees.Contracts
{
    /// <summary>
    /// A class claiming the wire name of the tests' <c>CompatMessage</c>: a mistake the subscriber must refuse. Here because
    /// the tests scan their own assembly with <c>AddMessages</c>, which would refuse it at startup.
    /// </summary>
    [Message("Compat.Events.CompatEvent")]
    public class CompatMessageTwin
    {
    }
}
