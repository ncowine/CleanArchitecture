using System.Threading.Tasks;
using Messaging;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>Configuration errors fail early. None of these need a broker.</summary>
    public class HostingConfigurationTests
    {
        /// <summary>Nothing listens here, so the bus never connects.</summary>
        private const int UnreachablePort = 1;

        [Fact]
        public async Task PublishingAMessageWithoutWireName_Throws()
        {
            await using (TestHost host = await TestHost.StartAsync("127.0.0.1", UnreachablePort, null, "compat.unreachable", "compat-unreachable"))
            {
                IMessagePublisher publisher = host.Services.GetRequiredService<IMessagePublisher>();

                System.InvalidOperationException error = Assert.Throws<System.InvalidOperationException>(() => publisher.Publish(new UnnamedMessage()));
                Assert.Contains("[Message(", error.Message);
            }
        }
    }
}
