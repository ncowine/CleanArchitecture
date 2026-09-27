using System;

namespace AppB.Events
{
    /// <summary>Same JSON shape as Golden/compat-payload.json.</summary>
    public class Customer
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
