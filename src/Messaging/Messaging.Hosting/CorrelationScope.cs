using System;

namespace Messaging.Hosting
{
    /// <summary>Returned by <see cref="CorrelationContext.Begin"/>: disposing it puts back the previous correlation ID.</summary>
    internal sealed class CorrelationScope : IDisposable
    {
        private readonly string previous;

        public CorrelationScope(string previous)
        {
            this.previous = previous;
        }

        public void Dispose()
        {
            CorrelationContext.Restore(previous);
        }
    }
}
