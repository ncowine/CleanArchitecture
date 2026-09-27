using System;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>Registered scoped: a new ID per DI scope, so tests can tell scopes apart.</summary>
    public sealed class ScopeMarker
    {
        public Guid Id { get; } = Guid.NewGuid();
    }
}
