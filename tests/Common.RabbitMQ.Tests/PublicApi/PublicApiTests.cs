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

            // The logger is a required constructor argument; the Logger properties and the Trace fallback are gone (2026-11-07).
            "ctor Common.RabbitMQ.RabbitMQService(Prism.Events.IEventAggregator, Common.RabbitMQ.RemoteEventRegistry)",

            // The router is built only by FromConfiguration: buses and the default come from the Messaging section (2026-11-07).
            "ctor Common.RabbitMQ.RabbitMQServiceRouter(Prism.Events.IEventAggregator, Common.RabbitMQ.RemoteEventRegistry)",
            "method Common.RabbitMQ.RabbitMQServiceRouter.AddBus(Common.RabbitMQ.RabbitMQConfig) : Common.RabbitMQ.RabbitMQService",

            // Events are found when first used; nothing passed assemblies to scan, so the registry is internal (2026-11-13).
            "sealed class Common.RabbitMQ.RemoteEventRegistry",
            "ctor Common.RabbitMQ.RemoteEventRegistry(System.Reflection.Assembly[])",
            "property Common.RabbitMQ.RemoteEventRegistry.Events : System.Collections.Generic.IReadOnlyCollection<Common.RabbitMQ.RemoteEventDescriptor> { get; }",
            "method Common.RabbitMQ.RemoteEventRegistry.TryGet(System.Type, out Common.RabbitMQ.RemoteEventDescriptor) : System.Boolean",
            "method Common.RabbitMQ.RemoteEventRegistry.TryGet(System.String, out Common.RabbitMQ.RemoteEventDescriptor) : System.Boolean",
        };

        /// <summary>Public API added after the baseline, each for a reason recorded in ADR 0002.</summary>
        private static readonly string[] ApprovedAdditions =
        {
            // Logging through an injected ILogger instead of the Log event (ADR 0001, amended 2026-11-03 and 2026-11-07).
            "ctor Common.RabbitMQ.RabbitMQService(Prism.Events.IEventAggregator, Microsoft.Extensions.Logging.ILogger)",

            // The publisher picks the bus: a default from configuration, or a named one (ADR 0002, update 2026-11-04).
            "ctor Common.RabbitMQ.RemoteEventDescriptor(System.Type, System.Type)",
            "property Common.RabbitMQ.RabbitMQServiceRouter.DefaultBus : System.String { get; }",
            "method Common.RabbitMQ.RabbitMQServiceRouter.GetRequiredBus(System.String) : Common.RabbitMQ.RabbitMQService",

            // Application startup in one call instead of a registry, a router and a loop over the configuration.
            "method static Common.RabbitMQ.RabbitMQServiceRouter.FromConfiguration(Prism.Events.IEventAggregator, Microsoft.Extensions.Configuration.IConfiguration, Microsoft.Extensions.Logging.ILogger) : Common.RabbitMQ.RabbitMQServiceRouter",

            // Only RemotePubSubEvent<T> travels; [Message] sets its wire name, so apps share no event assemblies (2026-11-07).
            "method static Common.RabbitMQ.RemotePubSubEventExtensions.PublishRemote<TPayload>(Common.RabbitMQ.RemotePubSubEvent<TPayload>, TPayload, Messaging.PublishOptions = null) : System.Void",

            // Subscribing starts receiving the event (ADR 0002, updates 2026-11-04 and 2026-11-05).
            "method virtual Common.RabbitMQ.RemotePubSubEvent<TPayload>.Subscribe(System.Action<TPayload>, Prism.Events.ThreadOption, System.Boolean, System.Predicate<TPayload>) : Prism.Events.SubscriptionToken",
            "method Common.RabbitMQ.RemotePubSubEvent<TPayload>.Subscribe(System.Action<TPayload>, Messaging.SubscribeOptions, Prism.Events.ThreadOption = PublisherThread, System.Boolean = False, System.Predicate<TPayload> = null) : Prism.Events.SubscriptionToken",

            // The last subscriber going stops receiving it (ADR 0002, updates 2026-11-04 and 2026-11-05).
            "method virtual Common.RabbitMQ.RemotePubSubEvent<TPayload>.Unsubscribe(Prism.Events.SubscriptionToken) : System.Void",
            "method virtual Common.RabbitMQ.RemotePubSubEvent<TPayload>.Unsubscribe(System.Action<TPayload>) : System.Void",
            "method virtual Common.RabbitMQ.RemotePubSubEvent<TPayload>.Publish(TPayload) : System.Void",
            "implements Common.RabbitMQ.RemotePubSubEvent<TPayload> : Common.RabbitMQ.IRemoteEvent",

            // Section 3: routing keys chosen by the publisher.
            "implements Common.RabbitMQ.RabbitMQService : Common.RabbitMQ.IRoutingKeyPublisher",
            "implements Common.RabbitMQ.RabbitMQServiceRouter : Common.RabbitMQ.IRoutingKeyPublisher",
            "method Common.RabbitMQ.RabbitMQService.Publish(System.Type, System.Object, System.String) : System.Void",
            "method Common.RabbitMQ.RabbitMQServiceRouter.Publish(System.Type, System.Object, System.String) : System.Void",

            // Remote only: send to other applications without raising the event locally.
            "method static Common.RabbitMQ.RemotePubSubEventExtensions.PublishRemoteOnly<TPayload>(Common.RabbitMQ.RemotePubSubEvent<TPayload>, TPayload, Messaging.PublishOptions = null) : System.Void",

            // Section 2: exchange type and subscriptions.
            "property Common.RabbitMQ.RabbitMQConfig.ExchangeType : System.String { get; set; }",
            "property Common.RabbitMQ.RabbitMQConfig.Subscriptions : System.Collections.Generic.List<Common.RabbitMQ.RabbitMQSubscription> { get; set; }",
            "class Common.RabbitMQ.RabbitMQSubscription",
            "ctor Common.RabbitMQ.RabbitMQSubscription()",
            "property Common.RabbitMQ.RabbitMQSubscription.Exchange : System.String { get; set; }",

            // One-way TLS to the broker.
            "property Common.RabbitMQ.RabbitMQConfig.UseTls : System.Boolean { get; set; }",
            "property Common.RabbitMQ.RabbitMQConfig.TlsServerName : System.String { get; set; }",
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

        [Fact]
        public void ServiceHasExactlyOnePublicConstructor()
        {
            // DryIoc rejects types with several public constructors, so adding one breaks resolution at runtime (ADR 0001).
            Assert.Single(typeof(RabbitMQService).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        }

        [Fact]
        public void RouterIsBuiltOnlyFromConfiguration()
        {
            // No constructor and no AddBus: an app can't assemble buses by hand and get them subtly wrong.
            Assert.Empty(typeof(RabbitMQServiceRouter).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
            Assert.Null(typeof(RabbitMQServiceRouter).GetMethod(nameof(RabbitMQServiceRouter.AddBus), BindingFlags.Public | BindingFlags.Instance));
            Assert.Null(typeof(RabbitMQServiceRouter).GetProperty(nameof(RabbitMQServiceRouter.DefaultBus)).GetSetMethod());
        }

        [Fact]
        public void IsNotEmpty()
        {
            // Guards the reader itself: an empty result would make the comparisons pass for anything.
            HashSet<string> api = PublicApiReader.Read(typeof(RabbitMQService).Assembly);

            Assert.Contains("ctor Common.RabbitMQ.RabbitMQService(Prism.Events.IEventAggregator, Microsoft.Extensions.Logging.ILogger)", api);
            Assert.Contains("method Common.RabbitMQ.RabbitMQService.Publish(System.Type, System.Object) : System.Void", api);
            Assert.Contains("implements Common.RabbitMQ.RabbitMQService : Common.RabbitMQ.IRabbitMQService", api);
        }
    }
}
