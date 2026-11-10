using Messaging;
using Messaging.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>Runs messaging's hosted services without a .NET host: the test harness (was Messaging.Hosting's MessagingClient).</summary>
    public sealed class TestClient : IDisposable
    {
        private readonly ServiceProvider provider;
        private readonly List<IHostedService> hostedServices;
        private bool started;
        private bool disposed;

        private TestClient(ServiceProvider provider)
        {
            this.provider = provider;
            hostedServices = provider.GetServices<IHostedService>().ToList();
        }

        public IServiceProvider Services => provider;

        public IMessagePublisher Publisher => provider.GetRequiredService<IMessagePublisher>();

        public static TestClient Create(Action<IServiceCollection> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            ServiceCollection services = new ServiceCollection();
            configure(services);
            services.TryAddSingleton<ILoggerFactory, NullLoggerFactory>();
            services.TryAdd(ServiceDescriptor.Singleton(typeof(ILogger<>), typeof(NullLogger<>)));

            return new TestClient(services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }));
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(TestClient));
            }

            if (started)
            {
                return;
            }

            started = true;
            foreach (IHostedService hostedService in hostedServices)
            {
                await hostedService.StartAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (!started)
            {
                return;
            }

            started = false;
            foreach (IHostedService hostedService in hostedServices.AsEnumerable().Reverse())
            {
                await hostedService.StopAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            Task.Run(() => StopAsync()).GetAwaiter().GetResult();
            provider.Dispose();
            disposed = true;
        }
    }
}
