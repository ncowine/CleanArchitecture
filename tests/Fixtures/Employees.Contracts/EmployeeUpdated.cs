using System;
using Messaging;

namespace Employees.Contracts
{
    /// <summary>
    /// An employee changed. Travels on the Legacy bus, where the net472 apps receive it.
    /// <para>Same body as the legacy <c>Common.Events.EmployeeUpdated</c> event's Employee payload; the wire name must never change.</para>
    /// </summary>
    [Message("Common.Events.EmployeeUpdated")]
    public class EmployeeUpdated
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public string Department { get; set; }

        /// <summary>Which application made the change.</summary>
        public string UpdatedBy { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
