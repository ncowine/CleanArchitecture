using System;
using Messaging;

namespace Employees.Contracts
{
    /// <summary>
    /// Sent by net8 apps when an employee is saved (Modern bus).
    /// <para>Same body as the legacy <c>Common.Events.EmployeeSaved</c> event's Employee payload; the wire name must never change.</para>
    /// </summary>
    [Message("Common.Events.EmployeeSaved")]
    public class EmployeeSaved
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public string Department { get; set; }

        /// <summary>Which application made the change.</summary>
        public string UpdatedBy { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
