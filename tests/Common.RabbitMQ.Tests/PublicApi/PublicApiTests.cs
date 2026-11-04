extern alias baseline;

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;
using BaselineService = baseline::Common.RabbitMQ.RabbitMQService;

namespace Common.RabbitMQ.Tests.PublicApi
{
    /// <summary>
    /// The public surface of Common.RabbitMQ against the baseline build: legacy apps compile and bind against it.
    /// Nothing may be removed or changed (ADR 0001, section 1) unless listed in <see cref="ApprovedRemovals"/>. Additions
    /// are allowed only as listed in <see cref="ApprovedAdditions"/>, so every change is a reviewed decision (ADR 0002,
    /// delivery step 7).
    /// </summary>
    public class PublicApiTests
    {
        /// <summary>Public API removed after the baseline. A legacy app that used one must change when it takes this version.</summary>
        private static readonly string[] ApprovedRemovals =
        {
            // Replaced by the Logger properties below (ADR 0001, amended 2026-11-03).
            "event Common.RabbitMQ.RabbitMQService.Log : System.EventHandler<System.String>",
            "event Common.RabbitMQ.RabbitMQServiceRouter.Log : System.EventHandler<System.String>",

            // Events no longer belong to a bus: the publisher picks it (ADR 0002, update 2026-11-04).
            "[System.AttributeUsageAttribute((System.AttributeTargets)4)]sealed class Common.RabbitMQ.RemoteEventAttribute : System.Attribute",
            "implements Common.RabbitMQ.RemoteEventAttribute : System.Runtime.InteropServices._Attribute",
            "ctor Common.RabbitMQ.RemoteEventAttribute(System.String)",
            "property Common.RabbitMQ.RemoteEventAttribute.Bus : System.String { get; }",
            "ctor Common.RabbitMQ.RemoteEventDescriptor(System.Type, System.Type, System.String)",
            "property Common.RabbitMQ.RemoteEventDescriptor.BusName : System.String { get; }",
            "method Common.RabbitMQ.RemoteEventRegistry.GetByBus(System.String) : System.Collections.Generic.IEnumerable<Common.RabbitMQ.RemoteEventDescriptor>",

            // PublishRemote takes PublishOptions instead of a service; PubSubEvent<T> covers RemotePubSubEvent<T> (ADR 0002, update 2026-11-04).
            "method static Common.RabbitMQ.RemotePubSubEventExtensions.PublishRemote<TPayload>(Common.RabbitMQ.RemotePubSubEvent<TPayload>, TPayload, Common.RabbitMQ.IRabbitMQService = null) : System.Void",
        };

        /// <summary>Public API added after the baseline, each for a reason recorded in ADR 0002.</summary>
        private static readonly string[] ApprovedAdditions =
        {
            // Logging through ILogger instead of the Log event (ADR 0001, amended 2026-11-03).
            "property Common.RabbitMQ.RabbitMQService.Logger : Microsoft.Extensions.Logging.ILogger { get; set; }",
            "property Common.RabbitMQ.RabbitMQServiceRouter.Logger : Microsoft.Extensions.Logging.ILogger { get; set; }",

            // The publisher picks the bus: a default from configuration, or a named one (ADR 0002, update 2026-11-04).
            "ctor Common.RabbitMQ.RemoteEventDescriptor(System.Type, System.Type)",
            "property Common.RabbitMQ.RabbitMQServiceRouter.DefaultBus : System.String { get; set; }",
            "method Common.RabbitMQ.RabbitMQServiceRouter.GetRequiredBus(System.String) : Common.RabbitMQ.RabbitMQService",

            // Application startup in one call instead of a registry, a router and a loop over the configuration.
            "method static Common.RabbitMQ.RabbitMQServiceRouter.FromConfiguration(Prism.Events.IEventAggregator, Microsoft.Extensions.Configuration.IConfiguration, System.Reflection.Assembly[]) : Common.RabbitMQ.RabbitMQServiceRouter",

            // Section 4: events found by assembly, plain PubSubEvent<T>.
            "method Common.RabbitMQ.RemoteEventRegistry.Add(System.Reflection.Assembly) : Common.RabbitMQ.RemoteEventRegistry",
            "method static Common.RabbitMQ.RemotePubSubEventExtensions.PublishRemote<TPayload>(Prism.Events.PubSubEvent<TPayload>, TPayload, Messaging.PublishOptions = null) : System.Void",

            // Section 3: routing keys chosen by the publisher.
            "interface Common.RabbitMQ.IRoutingKeyPublisher",
            "method abstract Common.RabbitMQ.IRoutingKeyPublisher.Publish(System.Type, System.Object, System.String) : System.Void",
            "implements Common.RabbitMQ.RabbitMQService : Common.RabbitMQ.IRoutingKeyPublisher",
            "implements Common.RabbitMQ.RabbitMQServiceRouter : Common.RabbitMQ.IRoutingKeyPublisher",
            "method Common.RabbitMQ.RabbitMQService.Publish(System.Type, System.Object, System.String) : System.Void",
            "method Common.RabbitMQ.RabbitMQServiceRouter.Publish(System.Type, System.Object, System.String) : System.Void",
            "property Common.RabbitMQ.RabbitMQConfig.RoutingKeys : System.Collections.Generic.Dictionary<System.String, System.String> { get; set; }",

            // Remote only: send to other applications without raising the event locally.
            "method static Common.RabbitMQ.RemotePubSubEventExtensions.PublishRemoteOnly<TPayload>(Prism.Events.PubSubEvent<TPayload>, TPayload, Messaging.PublishOptions = null) : System.Void",

            // Section 2: exchange type and subscriptions.
            "property Common.RabbitMQ.RabbitMQConfig.ExchangeType : System.String { get; set; }",
            "property Common.RabbitMQ.RabbitMQConfig.Subscriptions : System.Collections.Generic.List<Common.RabbitMQ.RabbitMQSubscription> { get; set; }",
            "class Common.RabbitMQ.RabbitMQSubscription",
            "ctor Common.RabbitMQ.RabbitMQSubscription()",
            "property Common.RabbitMQ.RabbitMQSubscription.Exchange : System.String { get; set; }",
            "property Common.RabbitMQ.RabbitMQSubscription.RoutingKeys : System.Collections.Generic.List<System.String> { get; set; }",
        };

        [Fact]
        public void NothingRemovedOrChanged()
        {
            HashSet<string> expected = PublicApiReader.Read(typeof(BaselineService).Assembly);
            HashSet<string> actual = PublicApiReader.Read(typeof(RabbitMQService).Assembly);

            List<string> removed = expected.Except(actual).Except(ApprovedRemovals).OrderBy(s => s).ToList();

            Assert.True(removed.Count == 0, "Public API removed or changed:\n  " + string.Join("\n  ", removed));
        }

        [Fact]
        public void OnlyApprovedAdditions()
        {
            HashSet<string> expected = PublicApiReader.Read(typeof(BaselineService).Assembly);
            HashSet<string> actual = PublicApiReader.Read(typeof(RabbitMQService).Assembly);

            List<string> unapproved = actual.Except(expected).Except(ApprovedAdditions).OrderBy(s => s).ToList();
            List<string> missing = ApprovedAdditions.Except(actual).OrderBy(s => s).ToList();

            Assert.True(
                unapproved.Count == 0 && missing.Count == 0,
                "Unapproved additions:\n  " + string.Join("\n  ", unapproved) + "\nApproved but not found:\n  " + string.Join("\n  ", missing));
        }

        [Theory]
        [InlineData(typeof(RabbitMQService))]
        [InlineData(typeof(RabbitMQServiceRouter))]
        public void ExactlyOnePublicConstructor(System.Type type)
        {
            // DryIoc rejects types with several public constructors, so adding one breaks resolution at runtime (ADR 0001).
            Assert.Single(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        }

        [Fact]
        public void IsNotEmpty()
        {
            // Guards the reader itself: an empty result would make the comparisons pass for anything.
            HashSet<string> api = PublicApiReader.Read(typeof(RabbitMQService).Assembly);

            Assert.Contains("ctor Common.RabbitMQ.RabbitMQService(Prism.Events.IEventAggregator, Common.RabbitMQ.RemoteEventRegistry)", api);
            Assert.Contains("method Common.RabbitMQ.RabbitMQService.Publish(System.Type, System.Object) : System.Void", api);
            Assert.Contains("implements Common.RabbitMQ.RabbitMQService : Common.RabbitMQ.IRabbitMQService", api);
        }
    }
}
