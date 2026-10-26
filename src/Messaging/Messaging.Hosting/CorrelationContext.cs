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
        // AsyncLocal: like a static field, but each chain of async calls has its own value. A value set here is seen by
        // everything this call goes on to run, across awaits and thread switches, while messages handled at the same
        // time each keep their own ID. A plain static would let them overwrite each other.
        private static readonly AsyncLocal<string> current = new AsyncLocal<string>();

        /// <summary>Null outside an operation.</summary>
        public static string Current => current.Value;

        /// <summary>
        /// Makes <paramref name="correlationId"/> current until the returned scope is disposed, then puts back the
        /// previous one. Use it in a <c>using</c> block.
        /// </summary>
        public static IDisposable Begin(string correlationId)
        {
            string previous = current.Value;
            current.Value = correlationId;
            return new CorrelationScope(previous);
        }

        internal static void Restore(string previous)
        {
            current.Value = previous;
        }
    }
}
