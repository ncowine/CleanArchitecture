using System.Globalization;
using BuildingBlocks.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CleanArch.UnitTests;

public class DataCacheDisposeTests
{
    /// <summary>
    /// A cache registered as itself AND as an IHostedService (the documented pre-warm pattern) is one instance tracked
    /// by both registrations, so the DI container disposes it twice when the app shuts down.
    /// EquipmentIntegrationEventsTests exercises that through a real container.
    /// </summary>
    [Fact]
    public void Disposing_twice_is_harmless()
    {
        var cache = new NumberCache();

        cache.Dispose();
        cache.Dispose();
    }

    private sealed class NumberCache() : DataCache<int, string>(NullLogger.Instance)
    {
        protected override Task<IReadOnlyDictionary<int, string>> FetchAsync(HashSet<int> keys, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<int, string>>(keys.ToDictionary(key => key, key => key.ToString(CultureInfo.InvariantCulture)));
    }
}
