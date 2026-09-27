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
    /// Nothing may be removed or changed (ADR 0001, section 1). Additions are allowed only as listed in
    /// <see cref="ApprovedAdditions"/>, so every new public member is a reviewed decision (ADR 0002, delivery step 7).
    /// </summary>
    public class PublicApiTests
    {
        /// <summary>Public API added after the baseline, each for a reason recorded in ADR 0002.</summary>
        private static readonly string[] ApprovedAdditions =
        {
            // Section 4: events found by assembly, plain PubSubEvent<T>.
            "method Common.RabbitMQ.RemoteEventRegistry.Add(System.Reflection.Assembly, System.String = null) : Common.RabbitMQ.RemoteEventRegistry",
            "method static Common.RabbitMQ.RemotePubSubEventExtensions.PublishRemote<TPayload>(Prism.Events.PubSubEvent<TPayload>, TPayload, Common.RabbitMQ.IRabbitMQService = null) : System.Void",

            // Section 3: routing keys chosen by the publisher.
            "method static Common.RabbitMQ.RemotePubSubEventExtensions.PublishRemoteTo<TPayload>(Prism.Events.PubSubEvent<TPayload>, System.String, TPayload, Common.RabbitMQ.IRabbitMQService = null) : System.Void",
            "interface Common.RabbitMQ.IRoutingKeyPublisher",
            "method abstract Common.RabbitMQ.IRoutingKeyPublisher.Publish(System.Type, System.Object, System.String) : System.Void",
            "implements Common.RabbitMQ.RabbitMQService : Common.RabbitMQ.IRoutingKeyPublisher",
            "implements Common.RabbitMQ.RabbitMQServiceRouter : Common.RabbitMQ.IRoutingKeyPublisher",
            "method Common.RabbitMQ.RabbitMQService.Publish(System.Type, System.Object, System.String) : System.Void",
            "method Common.RabbitMQ.RabbitMQServiceRouter.Publish(System.Type, System.Object, System.String) : System.Void",
            "property Common.RabbitMQ.RabbitMQConfig.RoutingKeys : System.Collections.Generic.Dictionary<System.String, System.String> { get; set; }",

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

            List<string> removed = expected.Except(actual).OrderBy(s => s).ToList();

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
            Assert.Contains("event Common.RabbitMQ.RabbitMQService.Log : System.EventHandler<System.String>", api);
            Assert.Contains("implements Common.RabbitMQ.RabbitMQService : Common.RabbitMQ.IRabbitMQService", api);
        }
    }
}
