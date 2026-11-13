using System;
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

        [Theory]
        [InlineData(typeof(EmployeeCacheRefreshed), "Common.Events.EmployeeCacheRefreshed")]
        [InlineData(typeof(EmployeeSaved), "Common.Events.EmployeeSaved")]
        [InlineData(typeof(EmployeeUpdated), "Common.Events.EmployeeUpdated")]
        public void CommonEvents_WireNames(Type eventType, string wireName)
        {
            Assert.Equal(wireName, new RemoteEventRegistry().FindOrAdd(eventType).EventName);
        }

        [Fact]
        public void LocalEvents_AreNotRemote()
        {
            Assert.Null(new RemoteEventRegistry().FindOrAdd(typeof(EmployeeSelected)));
        }
    }
}
