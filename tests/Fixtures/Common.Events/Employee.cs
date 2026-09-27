using System;

namespace Common.Events
{
    public class Employee
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public string Department { get; set; }

        /// <summary>Which application made the change.</summary>
        public string UpdatedBy { get; set; }

        public DateTime UpdatedAt { get; set; }

        public override string ToString()
        {
            return $"#{Id} {Name} ({Department}) by {UpdatedBy} at {UpdatedAt:HH:mm:ss}";
        }
    }
}
