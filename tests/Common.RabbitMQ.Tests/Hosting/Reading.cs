namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>One gauge measurement: the instrument, its connection role and the value.</summary>
    public sealed class Reading
    {
        public Reading(string instrument, string role, int value)
        {
            Instrument = instrument;
            Role = role;
            Value = value;
        }

        public string Instrument { get; }

        public string Role { get; }

        public int Value { get; }
    }
}
