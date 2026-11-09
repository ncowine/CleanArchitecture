using System;
using System.Collections.Generic;
using AppA.Events;
using Messaging.RabbitMQ;
using Xunit;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary>
    /// Tuples are rejected: their items are fields, which System.Text.Json (the one serializer) doesn't serialize, so they
    /// would arrive empty. Classes with properties and primitives are fine.
    /// </summary>
    public class TuplePayloadTests
    {
        [Theory]
        [InlineData(typeof(ValueTuple<Guid, string>))]
        [InlineData(typeof(Tuple<Guid, string>))]
        [InlineData(typeof(List<ValueTuple<int, int>>))]
        [InlineData(typeof(ValueTuple<int, int>[]))]
        [InlineData(typeof(Dictionary<string, ValueTuple<int, int>>))]
        [InlineData(typeof(TupleHolder))]
        public void Tuple_AnywhereInThePayload_Throws(Type payloadType)
        {
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => MessageSerializer.EnsureNoTuples(payloadType));

            Assert.Contains("tuple", ex.Message);
            Assert.Contains("{ get; set; } properties", ex.Message);
        }

        [Theory]
        [InlineData(typeof(int))]
        [InlineData(typeof(Guid))]
        [InlineData(typeof(bool))]
        [InlineData(typeof(string))]
        [InlineData(typeof(DateTime))]
        [InlineData(typeof(DayOfWeek))]
        [InlineData(typeof(Guid?))]
        [InlineData(typeof(List<string>))]
        [InlineData(typeof(Dictionary<string, int>))]
        [InlineData(typeof(Order))]
        public void ClassesAndPrimitives_AreAccepted(Type payloadType)
        {
            MessageSerializer.EnsureNoTuples(payloadType);
        }

        [Fact]
        public void LegacyEvent_WithATuplePayload_ThrowsWhenFirstUsed()
        {
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => new RemoteEventDescriptor(typeof(OrderPlaced), typeof(ValueTuple<Guid, string>)));

            Assert.Contains("is a tuple", ex.Message);
        }
    }
}
