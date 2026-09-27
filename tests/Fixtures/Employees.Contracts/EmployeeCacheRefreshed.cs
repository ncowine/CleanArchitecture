using System;
using Messaging;

namespace Employees.Contracts
{
    /// <summary>
    /// Sent by the API after its employee cache changed, so clients can react (Modern bus).
    /// <para>Same body as the legacy <c>Common.Events.EmployeeCacheRefreshed</c> event's Employee payload; the wire name must never change.</para>
    /// </summary>
    [Message("Common.Events.EmployeeCacheRefreshed")]
    public class EmployeeCacheRefreshed
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public string Department { get; set; }

        /// <summary>Which application made the change.</summary>
        public string UpdatedBy { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
