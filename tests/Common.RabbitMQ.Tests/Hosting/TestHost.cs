using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Common.RabbitMQ.Tests.Broker;
using Compat.Events;
using Messaging.Hosting;
using Messaging.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>
    /// A server as the API builds it: AddMessaging with one bus on the test exchange, started through its hosted
    /// services. <see cref="HandledMessages"/> and a scoped <see cref="ScopeMarker"/> are registered for the test handler.
    /// </summary>
    public sealed class TestHost : IAsyncDisposable
    {
        private readonly ServiceProvider provider;
        private readonly List<IHostedService> hostedServices;

        private TestHost(ServiceProvider provider, List<IHostedService> hostedServices)
        {
            this.provider = provider;
            this.hostedServices = hostedServices;
        }

        public IServiceProvider Services => provider;

        public RabbitMQBus Bus => provider.GetRequiredKeyedService<RabbitMQBus>(CompatBus.Name);

        public HandledMessages Handled => provider.GetRequiredService<HandledMessages>();

        public static Task<TestHost> Start(TestBus bus, string clientName, Action<MessagingBuilder> configure, Action<RabbitMQBusOptions> configureBus = null)
        {
            return Start(bus.Broker.HostName, bus.Broker.Port, bus.Broker, bus.ExchangeName, clientName, configure, configureBus);
        }

        /// <summary>Starts a host whose bus points at <paramref name="hostName"/>:<paramref name="port"/>.</summary>
        public static async Task<TestHost> Start(string hostName, int port, BrokerSettings credentials, string exchangeName, string clientName, Action<MessagingBuilder> configure, Action<RabbitMQBusOptions> configureBus = null)
        {
            ServiceCollection services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<HandledMessages>();
            services.AddScoped<ScopeMarker>();
            services.AddHealthChecks().AddMessaging();
            services.AddMessaging(messaging =>
            {
                messaging.AddBus(CompatBus.Name, options =>
                {
                    options.HostName = hostName;
                    options.Port = port;
                    options.VirtualHost = credentials?.VirtualHost ?? "/";
                    options.UserName = credentials?.UserName ?? "guest";
                    options.Password = credentials?.Password ?? "guest";
                    options.ExchangeName = exchangeName;
                    options.ClientName = clientName;
                    options.ReconnectDelay = TimeSpan.FromSeconds(1);
                    options.OutstandingPollInterval = TimeSpan.FromMilliseconds(20);
                    configureBus?.Invoke(options);
                });
                configure(messaging);
            });

            ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            List<IHostedService> hostedServices = new List<IHostedService>(provider.GetServices<IHostedService>());
            TestHost host = new TestHost(provider, hostedServices);

            try
            {
                foreach (IHostedService hostedService in hostedServices)
                {
                    await hostedService.StartAsync(CancellationToken.None);
                }
            }
            catch
            {
                await host.DisposeAsync();
                throw;
            }

            return host;
        }

        public async Task WaitUntilConnected()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (!Bus.IsConsumerConnected || !Bus.IsPublisherConnected)
            {
                if (stopwatch.Elapsed > TestBus.Timeout)
                {
                    throw new TimeoutException($"Host bus did not connect within {TestBus.Timeout.TotalSeconds}s.");
                }

                await Task.Delay(50);
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (IHostedService hostedService in hostedServices)
            {
                await hostedService.StopAsync(CancellationToken.None);
            }

            await provider.DisposeAsync();
        }
    }
}
