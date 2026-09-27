using System;

namespace Messaging
{
    public sealed class ConnectionStateChange
    {
        public ConnectionStateChange(string busName, ConnectionRole role, ConnectionStatus status, string reason, Exception error)
        {
            BusName = busName;
            Role = role;
            Status = status;
            Reason = reason;
            Error = error;
        }

        public string BusName { get; }

        public ConnectionRole Role { get; }

        public ConnectionStatus Status { get; }

        /// <summary>The broker's reply text when a connection is lost; otherwise null.</summary>
        public string Reason { get; }

        /// <summary>Why connecting failed; otherwise null.</summary>
        public Exception Error { get; }
    }
}
