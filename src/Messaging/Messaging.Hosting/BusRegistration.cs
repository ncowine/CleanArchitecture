namespace Messaging.Hosting
{
    internal sealed class BusRegistration
    {
        public BusRegistration(string name, IMessageSerializer serializer)
        {
            Name = name;
            Serializer = serializer;
        }

        public string Name { get; }

        /// <summary>Null means the System.Text.Json default.</summary>
        public IMessageSerializer Serializer { get; }
    }
}
