using System;

namespace Messaging
{
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
