using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>Starts and stops hosted services in the phases a .NET host uses, including <see cref="IHostedLifecycleService"/>.</summary>
    public static class HostedServices
    {
        public static async Task StartAsync(IReadOnlyList<IHostedService> services, CancellationToken cancellationToken = default)
        {
            foreach (IHostedLifecycleService service in services.OfType<IHostedLifecycleService>())
            {
                await service.StartingAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (IHostedService service in services)
            {
                await service.StartAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (IHostedLifecycleService service in services.OfType<IHostedLifecycleService>())
            {
                await service.StartedAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public static async Task StopAsync(IReadOnlyList<IHostedService> services, CancellationToken cancellationToken = default)
        {
            List<IHostedService> reversed = services.Reverse().ToList();

            foreach (IHostedLifecycleService service in reversed.OfType<IHostedLifecycleService>())
            {
                await service.StoppingAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (IHostedService service in reversed)
            {
                await service.StopAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (IHostedLifecycleService service in reversed.OfType<IHostedLifecycleService>())
            {
                await service.StoppedAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
