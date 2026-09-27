using System;

namespace Common.RabbitMQ
{
    /// <summary>
    /// Declares which bus a <see cref="RemotePubSubEvent{TPayload}"/> travels over. Required on every remote event.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class RemoteEventAttribute : Attribute
    {
        public RemoteEventAttribute(string bus)
        {
            Bus = bus;
        }

        public string Bus { get; }
    }
}
