using System.Collections.Generic;
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
        public void CommonEvents_WireNamesAndBuses()
        {
            RemoteEventRegistry registry = new RemoteEventRegistry(typeof(EmployeeUpdated).Assembly);

            Dictionary<string, string> actual = registry.Events.ToDictionary(e => e.EventName, e => e.BusName);

            Dictionary<string, string> expected = new Dictionary<string, string>
            {
                ["Common.Events.EmployeeUpdated"] = "Legacy",
                ["Common.Events.EmployeeSaved"] = "Modern",
                ["Common.Events.EmployeeCacheRefreshed"] = "Modern",
            };

            Assert.Equal(expected.OrderBy(p => p.Key), actual.OrderBy(p => p.Key));
        }

        [Fact]
        public void LocalEvents_AreNotRemote()
        {
            RemoteEventRegistry registry = new RemoteEventRegistry(typeof(EmployeeSelected).Assembly);

            Assert.False(registry.TryGet(typeof(EmployeeSelected), out RemoteEventDescriptor descriptor));
        }
    }
}
