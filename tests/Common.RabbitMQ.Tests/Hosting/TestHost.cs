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
    /// services. <paramref name="subscribe"/> runs before the host starts, as a hosted service's subscriptions would.
    /// <see cref="HandledMessages"/>, a scoped <see cref="ScopeMarker"/> and <see cref="CompatMessageHandler"/> are registered.
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

        public static Task<TestHost> StartAsync(TestBus bus, string clientName, Action<IServiceProvider> subscribe = null, Action<RabbitMQBusOptions> configureBus = null)
        {
            return StartAsync(bus.Broker.HostName, bus.Broker.Port, bus.Broker, bus.ExchangeName, clientName, subscribe, configureBus);
        }

        /// <summary>Starts a host whose bus points at <paramref name="hostName"/>:<paramref name="port"/>.</summary>
        public static async Task<TestHost> StartAsync(string hostName, int port, BrokerSettings credentials, string exchangeName, string clientName, Action<IServiceProvider> subscribe = null, Action<RabbitMQBusOptions> configureBus = null)
        {
            ServiceCollection services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<HandledMessages>();
            services.AddScoped<ScopeMarker>();
            services.AddScoped<CompatMessageHandler>();
            services.AddTestMessaging(CompatBus.Name, (CompatBus.Name, options =>
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
            }));

            ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            List<IHostedService> hostedServices = new List<IHostedService>(provider.GetServices<IHostedService>());
            TestHost host = new TestHost(provider, hostedServices);

            try
            {
                subscribe?.Invoke(provider);
                await HostedServices.StartAsync(hostedServices);
            }
            catch
            {
                await host.DisposeAsync();
                throw;
            }

            return host;
        }

        public async Task WaitUntilConnectedAsync()
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
            await HostedServices.StopAsync(hostedServices, CancellationToken.None);
            await provider.DisposeAsync();
        }
    }
}
