using System;

namespace AppA.Events
{
    /// <summary>Same JSON shape as Golden/compat-payload.json.</summary>
    public class Order
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
