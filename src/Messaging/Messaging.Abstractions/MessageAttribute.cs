using System;

namespace Messaging
{
    /// <summary>
    /// A message's stable identity on the wire: its routing key and event-type header (ADR 0001, section 2).
    /// Never derived from the CLR type, so classes can be renamed and moved freely. A message that legacy apps
    /// receive uses the legacy event's full name, for example <c>[Message("Common.Events.EmployeeUpdated")]</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class MessageAttribute : Attribute
    {
        public MessageAttribute(string wireName)
        {
            if (string.IsNullOrWhiteSpace(wireName))
            {
                throw new ArgumentException("A wire name is required.", nameof(wireName));
            }

            WireName = wireName;
        }

        public string WireName { get; }
    }
}
