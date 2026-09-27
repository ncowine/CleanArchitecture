using BuildingBlocks.Correlation;
using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using BuildingBlocks.Outbox.Messaging;
using Messaging.Hosting;
using Messaging.RabbitMQ;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using MessagingCorrelation = Messaging.Hosting.CorrelationContext;

namespace CleanArch.Api.IntegrationTests;

/// <summary>
/// The outbox sending to RabbitMQ (docs/messaging/adr/0003), with the broker replaced by a fake
/// <see cref="IConfirmedMessagePublisher"/>. The real <see cref="OutboxWriter{TContext}"/> writes the row and the
/// real outbox processor delivers it, against a temp SQLite file.
/// </summary>
public sealed class OutboxMessagingTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"outbox-messaging-{Guid.NewGuid():N}.db");
    private readonly FakeConfirmedPublisher _publisher = new();
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediator();
        services.AddDbContext<OutboxTestDbContext>(options => options.UseSqlite($"Data Source={_dbPath}"));
        services.AddSingleton<IConfirmedMessagePublisher>(_publisher);
        services.AddOutboxWriter<OutboxTestDbContext>();
        services.AddOutboxPublishing<OutboxTestDbContext>(typeof(EmployeeRetired));
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task Broker_outage_defers_the_message_without_using_up_its_attempts()
    {
        // More failures than the outbox's 3 attempts: counted as failures, the message would be dead-lettered.
        _publisher.UnavailableTimes = 3;
        var rowId = await EnqueueAsync(new EmployeeRetired { EmployeeId = 7, Name = "Ada" });

        var hosted = _provider.GetServices<IHostedService>().ToList();
        foreach (var service in hosted)
        {
            await service.StartAsync(CancellationToken.None);
        }

        try
        {
            var row = await WaitForAsync(message => message.ProcessedOnUtc != null || message.DeadLetteredOnUtc != null);

            Assert.NotNull(row.ProcessedOnUtc);
            Assert.Null(row.DeadLetteredOnUtc);
            Assert.Equal(1, row.Attempts);
            Assert.Null(row.Error);

            var (message, messageId, correlationId) = Assert.Single(_publisher.Published);
            var retired = Assert.IsType<EmployeeRetired>(message);
            Assert.Equal(7, retired.EmployeeId);
            Assert.Equal("Ada", retired.Name);
            Assert.Equal(rowId.ToString("N"), messageId);

            // The request's correlation ID, stored on the row by the writer, is what the message is sent under.
            Assert.NotNull(row.CorrelationId);
            Assert.Equal(row.CorrelationId, correlationId);
        }
        finally
        {
            foreach (var service in hosted)
            {
                await service.StopAsync(CancellationToken.None);
            }
        }
    }

    [Fact]
    public async Task Dispatcher_turns_a_broker_outage_into_a_deferral()
    {
        _publisher.UnavailableTimes = 1;
        var dispatcher = CreateDispatcher();

        var deferred = await Assert.ThrowsAsync<OutboxDeliveryDeferredException>(() =>
            dispatcher.DispatchAsync(Guid.NewGuid(), nameof(EmployeeRetired), """{"EmployeeId":1,"Name":"x"}""", default));

        Assert.IsType<BrokerUnavailableException>(deferred.InnerException);
    }

    [Fact]
    public async Task Dispatcher_publishes_under_the_scopes_correlation_id_and_then_restores_the_previous_one()
    {
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICorrelationContext>().Set("req-42");
        var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher<OutboxTestDbContext>>();

        await dispatcher.DispatchAsync(Guid.NewGuid(), nameof(EmployeeRetired), """{"EmployeeId":1,"Name":"x"}""", default);

        Assert.Equal("req-42", Assert.Single(_publisher.Published).CorrelationId);
        Assert.Null(MessagingCorrelation.Current);
    }

    [Fact]
    public async Task Dispatcher_rejects_a_type_it_was_not_given()
    {
        var dispatcher = CreateDispatcher();

        Assert.False(dispatcher.CanDispatch("SomethingElse"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.DispatchAsync(Guid.NewGuid(), "SomethingElse", "{}", default));
        Assert.Contains("AddOutboxPublishing<OutboxTestDbContext>", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Message_types_with_the_same_name_are_rejected()
    {
        // The outbox stores only the short type name, so these two couldn't be told apart.
        var error = Assert.Throws<ArgumentException>(() =>
            new OutboxMessageTypes<OutboxTestDbContext>([typeof(EmployeeRetired), typeof(Other.EmployeeRetired)]));

        Assert.Contains(typeof(Other.EmployeeRetired).FullName!, error.Message, StringComparison.Ordinal);
    }

    private MessagingOutboxDispatcher<OutboxTestDbContext> CreateDispatcher() =>
        new(_publisher, new OutboxMessageTypes<OutboxTestDbContext>([typeof(EmployeeRetired)]), new FixedCorrelation("req-1"));

    private async Task<Guid> EnqueueAsync<TEvent>(TEvent integrationEvent) where TEvent : class
    {
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IOutbox>().Enqueue(integrationEvent);
        var db = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
        await db.SaveChangesAsync();
        return (await db.Set<OutboxMessage>().SingleAsync()).Id;
    }

    private async Task<OutboxMessage> WaitForAsync(Func<OutboxMessage, bool> condition)
    {
        // The processor polls every 2 seconds; four polls are needed here.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            using var scope = _provider.CreateScope();
            var row = await scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>()
                .Set<OutboxMessage>().AsNoTracking().SingleAsync();
            if (condition(row))
            {
                return row;
            }

            Assert.True(DateTime.UtcNow < deadline, $"Timed out. Attempts: {row.Attempts}, error: {row.Error}.");
            await Task.Delay(100);
        }
    }

    public sealed class OutboxTestDbContext(DbContextOptions<OutboxTestDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ApplyOutboxConfiguration();
    }

    [global::Messaging.Message("CleanArch.Tests.EmployeeRetired")]
    public sealed class EmployeeRetired
    {
        public int EmployeeId { get; set; }

        public string Name { get; set; } = "";
    }

    private sealed class FakeConfirmedPublisher : IConfirmedMessagePublisher
    {
        private int _unavailableTimes;

        public int UnavailableTimes
        {
            set => _unavailableTimes = value;
        }

        /// <summary>Each publish, with the messaging library's correlation ID in effect at the time.</summary>
        public List<(object Message, string? MessageId, string? CorrelationId)> Published { get; } = [];

        public Task PublishConfirmedAsync(object message, string? messageId, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Decrement(ref _unavailableTimes) >= 0)
            {
                throw new BrokerUnavailableException("Bus 'Test' is not connected to the broker.");
            }

            lock (Published)
            {
                Published.Add((message, messageId, MessagingCorrelation.Current));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FixedCorrelation(string correlationId) : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = correlationId;

        public void Set(string correlationId) => CorrelationId = correlationId;
    }
}

public static class Other
{
    public sealed class EmployeeRetired;
}
