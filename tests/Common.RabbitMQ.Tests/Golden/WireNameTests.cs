using System.Linq;
using Common.Events;
using Xunit;

namespace Common.RabbitMQ.Tests.Golden
{
    /// <summary>
    /// Wire names (routing key and event-type header) and header names are frozen (ADR 0001, sections 2 and 3).
    /// Renaming or moving an event class breaks every running app that listens for it.
    /// </summary>
    public class WireNameTests
    {
        [Fact]
        public void HeaderNames()
        {
            Assert.Equal("event-type", MessageHeaders.EventType);
            Assert.Equal("source-id", MessageHeaders.SourceId);
        }

        [Fact]
        public void CommonEvents_WireNames()
        {
            RemoteEventRegistry registry = new RemoteEventRegistry(typeof(EmployeeUpdated).Assembly);

            string[] expected =
            {
                "Common.Events.EmployeeCacheRefreshed",
                "Common.Events.EmployeeSaved",
                "Common.Events.EmployeeUpdated",
            };

            Assert.Equal(expected, registry.Events.Select(e => e.EventName).OrderBy(n => n, System.StringComparer.Ordinal));
        }

        [Fact]
        public void LocalEvents_AreNotRemote()
        {
            RemoteEventRegistry registry = new RemoteEventRegistry(typeof(EmployeeSelected).Assembly);

            Assert.False(registry.TryGet(typeof(EmployeeSelected), out RemoteEventDescriptor descriptor));
        }
    }
}
