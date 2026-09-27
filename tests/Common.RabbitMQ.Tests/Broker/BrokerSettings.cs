using System;
using System.Threading.Tasks;
using RabbitMQ.Client;
using Xunit;

namespace Common.RabbitMQ.Tests.Broker
{
    /// <summary>
    /// The broker the compatibility tests run against, read from the environment:
    /// <c>RABBITMQ_HOST</c> (localhost), <c>RABBITMQ_PORT</c> (5672), <c>RABBITMQ_VHOST</c> (/),
    /// <c>RABBITMQ_USER</c> and <c>RABBITMQ_PASSWORD</c> (guest).
    /// <para>
    /// Tests skip when the broker can't be reached, unless <c>RABBITMQ_TESTS_REQUIRED=1</c> (for CI), in which case
    /// they fail (ADR 0001, delivery plan step 0).
    /// </para>
    /// </summary>
    public sealed class BrokerSettings
    {
        private static readonly Lazy<Task<Exception>> probe = new Lazy<Task<Exception>>(Probe);

        private BrokerSettings()
        {
            HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";
            Port = int.TryParse(Environment.GetEnvironmentVariable("RABBITMQ_PORT"), out int port) ? port : 5672;
            VirtualHost = Environment.GetEnvironmentVariable("RABBITMQ_VHOST") ?? "/";
            UserName = Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? "guest";
            Password = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? "guest";
        }

        public static BrokerSettings Instance { get; } = new BrokerSettings();

        public string HostName { get; }

        public int Port { get; }

        public string VirtualHost { get; }

        public string UserName { get; }

        public string Password { get; }

        private static bool IsRequired => Environment.GetEnvironmentVariable("RABBITMQ_TESTS_REQUIRED") == "1";

        /// <summary>Skips the calling test when there is no broker, or fails it when a broker is required.</summary>
        public static async Task<BrokerSettings> Require()
        {
            Exception error = await probe.Value.ConfigureAwait(false);
            if (error != null)
            {
                string message = $"No RabbitMQ broker at {Instance.HostName}:{Instance.Port}: {error.Message}";
                if (IsRequired)
                {
                    throw new InvalidOperationException(message + " (RABBITMQ_TESTS_REQUIRED=1)", error);
                }

                Assert.Skip(message);
            }

            return Instance;
        }

        public ConnectionFactory CreateConnectionFactory()
        {
            return new ConnectionFactory
            {
                HostName = HostName,
                Port = Port,
                VirtualHost = VirtualHost,
                UserName = UserName,
                Password = Password,
                RequestedConnectionTimeout = TimeSpan.FromSeconds(3),
                AutomaticRecoveryEnabled = false,
            };
        }

        public RabbitMQConfig CreateCurrentConfig(string busName, string exchangeName, string clientName)
        {
            return new RabbitMQConfig
            {
                BusName = busName,
                HostName = HostName,
                Port = Port,
                VirtualHost = VirtualHost,
                UserName = UserName,
                Password = Password,
                ExchangeName = exchangeName,
                ClientName = clientName,
                ReconnectDelaySeconds = 1,
                OutstandingPollIntervalMilliseconds = 20,
            };
        }

        private static async Task<Exception> Probe()
        {
            try
            {
                using (IConnection connection = await Instance.CreateConnectionFactory().CreateConnectionAsync("Common.RabbitMQ.Tests probe").ConfigureAwait(false))
                {
                    await connection.CloseAsync().ConfigureAwait(false);
                }

                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }
    }
}
