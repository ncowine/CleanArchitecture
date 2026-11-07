using System.Reflection;
using Messaging;
using Messaging.RabbitMQ;
using Xunit;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>
    /// What a nullable-enabled caller sees: optional properties are <c>string?</c>, required ones <c>string</c>, so nobody
    /// thinks they must fill in every property. In Hosting because NullabilityInfoContext needs .NET 6 or later.
    /// </summary>
    public class OptionsNullabilityTests
    {
        [Theory]
        [InlineData(typeof(PublishOptions), nameof(PublishOptions.Bus), NullabilityState.Nullable)]
        [InlineData(typeof(PublishOptions), nameof(PublishOptions.RoutingKey), NullabilityState.Nullable)]
        [InlineData(typeof(SubscribeOptions), nameof(SubscribeOptions.Bus), NullabilityState.Nullable)]
        [InlineData(typeof(SubscribeOptions), nameof(SubscribeOptions.Exchange), NullabilityState.Nullable)]
        [InlineData(typeof(SubscribeOptions), nameof(SubscribeOptions.RoutingKey), NullabilityState.NotNull)]
        [InlineData(typeof(SubscriptionOptions), nameof(SubscriptionOptions.Exchange), NullabilityState.NotNull)]
        public void OnlyRequiredPropertiesAreNonNullable(System.Type type, string property, NullabilityState expected)
        {
            NullabilityInfo info = new NullabilityInfoContext().Create(type.GetProperty(property));

            Assert.Equal(expected, info.WriteState);
        }
    }
}
