using System;
using System.Threading;

namespace Messaging.Hosting
{
    /// <summary>
    /// The correlation ID of the current operation. Messages published inside it carry it in the correlation-id
    /// header. Handlers run inside the incoming message's correlation ID, so a chain of messages shares one; an HTTP
    /// middleware can <see cref="Begin"/> one per request.
    /// </summary>
    public static class CorrelationContext
    {
        private static readonly AsyncLocal<string> current = new AsyncLocal<string>();

        /// <summary>Null outside an operation.</summary>
        public static string Current => current.Value;

        /// <summary>Makes <paramref name="correlationId"/> current until the returned scope is disposed.</summary>
        public static IDisposable Begin(string correlationId)
        {
            string previous = current.Value;
            current.Value = correlationId;
            return new Scope(previous);
        }

        private sealed class Scope : IDisposable
        {
            private readonly string previous;

            public Scope(string previous)
            {
                this.previous = previous;
            }

            public void Dispose()
            {
                current.Value = previous;
            }
        }
    }
}
