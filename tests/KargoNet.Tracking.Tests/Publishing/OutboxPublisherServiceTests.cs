using KargoNet.Messaging.Publishing;
using KargoNet.Shipment.Api.Data;
using KargoNet.Shipment.Api.Entities;
using KargoNet.Shipment.Api.Publishing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace KargoNet.Tracking.Tests.Publishing;

using OutboxMessage = KargoNet.Shipment.Api.Entities.OutboxMessage;
using OutboxStatus = KargoNet.Shipment.Api.Entities.OutboxStatus;

public class OutboxPublisherServiceTests
{
    private sealed record PublishCall(string Exchange, string RoutingKey, byte[] Body);

    private sealed class FakeOutboxChannel : IOutboxChannel
    {
        private readonly PublishOutcome _outcome;
        public List<PublishCall> Calls { get; } = [];

        public FakeOutboxChannel(PublishOutcome outcome)
            => _outcome = outcome;

        public Task<PublishOutcome> PublishAsync(string exchange, string routingKey, ReadOnlyMemory<byte> body, IDictionary<string, object?>? headers)
        {
            Calls.Add(new PublishCall(exchange, routingKey, body.ToArray()));
            return Task.FromResult(_outcome);
        }
    }

    private static (ShipmentDbContext Db, OutboxPublisherService Service, FakeOutboxChannel Channel) CreateService(
        PublishOutcome outcome,
        string databaseName)
    {
        var options = new DbContextOptionsBuilder<ShipmentDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var db = new ShipmentDbContext(options);
        var channel = new FakeOutboxChannel(outcome);
        var service = new OutboxPublisherService(db, new MessagePublisher(channel), NullLogger<OutboxPublisherService>.Instance);

        return (db, service, channel);
    }

    private static OutboxMessage CreateRow(DateTimeOffset createdAt, string? payloadJson = null, OutboxStatus status = OutboxStatus.Pending)
        => new()
        {
            Id = Guid.NewGuid(),
            EventType = "shipment.created",
            PayloadJson = payloadJson ?? """{"eventId":"a56a5a1b-2222-4333-8444-555566667777","eventType":"shipment.created","occurredAt":"2026-11-19T10:00:00Z","payload":{}}""",
            Status = status,
            CreatedAt = createdAt,
        };

    [Fact]
    public async Task PendingRow_PublishesVerbatimAndIsMarkedPublished()
    {
        var (db, service, channel) = CreateService(new PublishOutcome(true, 1), "outbox-test-1");
        var row = CreateRow(new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero));
        db.Set<OutboxMessage>().Add(row);
        await db.SaveChangesAsync();

        var published = await service.PollOnceAsync(CancellationToken.None);

        Assert.Equal(1, published);
        var call = Assert.Single(channel.Calls);
        Assert.Equal("kargonet.shipments", call.Exchange);
        Assert.Equal("shipment", call.RoutingKey);
        Assert.Equal(row.PayloadJson, System.Text.Encoding.UTF8.GetString(call.Body));

        var saved = await db.Set<OutboxMessage>().SingleAsync();
        Assert.Equal(OutboxStatus.Published, saved.Status);
        Assert.NotNull(saved.PublishedAt);
    }

    [Fact]
    public async Task UnconfirmedPublish_LeavesRowPendingAndRetriesOnNextPoll()
    {
        var (db, service, channel) = CreateService(new PublishOutcome(false, 7), "outbox-test-2");
        var row = CreateRow(new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero));
        db.Set<OutboxMessage>().Add(row);
        await db.SaveChangesAsync();

        var firstPoll = await service.PollOnceAsync(CancellationToken.None);

        Assert.Equal(0, firstPoll);
        var saved = await db.Set<OutboxMessage>().SingleAsync();
        Assert.Equal(OutboxStatus.Pending, saved.Status);
        Assert.Null(saved.PublishedAt);

        var secondPoll = await service.PollOnceAsync(CancellationToken.None);

        Assert.Equal(0, secondPoll);
        Assert.Equal(2, channel.Calls.Count);
        Assert.Equal(row.Id, (await db.Set<OutboxMessage>().SingleAsync()).Id);
    }

    [Fact]
    public async Task PublishedRow_IsSkippedWithoutPublishCall()
    {
        var (db, service, channel) = CreateService(new PublishOutcome(true, 1), "outbox-test-3");
        db.Set<OutboxMessage>().Add(CreateRow(
            new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero),
            status: OutboxStatus.Published));
        await db.SaveChangesAsync();

        var published = await service.PollOnceAsync(CancellationToken.None);

        Assert.Equal(0, published);
        Assert.Empty(channel.Calls);
    }

    [Fact]
    public async Task BatchIsCappedAtFifty_OldestFirst()
    {
        var (db, service, channel) = CreateService(new PublishOutcome(true, 1), "outbox-test-4");
        var baseTime = new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
        var rows = Enumerable.Range(0, 51)
            .Select(i => CreateRow(baseTime.AddSeconds(i)))
            .ToList();
        db.Set<OutboxMessage>().AddRange(rows);
        await db.SaveChangesAsync();

        var published = await service.PollOnceAsync(CancellationToken.None);

        Assert.Equal(50, published);
        Assert.Equal(50, channel.Calls.Count);

        var pending = await db.Set<OutboxMessage>().Where(o => o.Status == OutboxStatus.Pending).ToListAsync();
        var remaining = Assert.Single(pending);
        Assert.Equal(rows[50].Id, remaining.Id); // the newest row stays Pending
        var publishedIds = (await db.Set<OutboxMessage>().Where(o => o.Status == OutboxStatus.Published).Select(o => o.Id).ToListAsync())
            .ToHashSet();
        Assert.Equal(50, publishedIds.Count);
    }

    private sealed class ScriptedOutboxChannel : IOutboxChannel
    {
        private readonly Func<int, PublishOutcome> _script;
        public List<PublishCall> Calls { get; } = [];

        public ScriptedOutboxChannel(Func<int, PublishOutcome> script)
            => _script = script;

        public Task<PublishOutcome> PublishAsync(string exchange, string routingKey, ReadOnlyMemory<byte> body, IDictionary<string, object?>? headers)
        {
            var outcome = _script(Calls.Count);
            Calls.Add(new PublishCall(exchange, routingKey, body.ToArray()));
            return Task.FromResult(outcome);
        }
    }

    [Fact]
    public async Task FailedFirstRow_DoesNotDetachRemainingRows_TheyAreMarkedPublished()
    {
        var options = new DbContextOptionsBuilder<ShipmentDbContext>()
            .UseInMemoryDatabase("outbox-test-cr01")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new ShipmentDbContext(options);
        var channel = new ScriptedOutboxChannel(i => i == 0
            ? new PublishOutcome(false, 7)   // first row's publish is unconfirmed
            : new PublishOutcome(true, 1));
        var service = new OutboxPublisherService(db, new MessagePublisher(channel), NullLogger<OutboxPublisherService>.Instance);

        var baseTime = new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
        var rows = Enumerable.Range(0, 3).Select(i => CreateRow(baseTime.AddSeconds(i))).ToList();
        db.Set<OutboxMessage>().AddRange(rows);
        await db.SaveChangesAsync();

        var published = await service.PollOnceAsync(CancellationToken.None);

        // Row 1 stays Pending; rows 2-3 were confirmed AND their Published
        // marks persisted (no ChangeTracker.Clear() detach of the batch).
        Assert.Equal(2, published);
        Assert.Equal(3, channel.Calls.Count);

        var savedRows = await db.Set<OutboxMessage>().ToListAsync();
        Assert.Equal(OutboxStatus.Pending, savedRows.Single(r => r.Id == rows[0].Id).Status);
        foreach (var row in rows.Skip(1))
        {
            var saved = savedRows.Single(r => r.Id == row.Id);
            Assert.Equal(OutboxStatus.Published, saved.Status);
            Assert.NotNull(saved.PublishedAt);
        }
    }

    private sealed class FailingShipmentDbContext : ShipmentDbContext
    {
        public int PollAttempts;

        public FailingShipmentDbContext(string databaseName)
            : base(new DbContextOptionsBuilder<ShipmentDbContext>()
                .UseInMemoryDatabase(databaseName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options)
        {
        }

        public override DbSet<T> Set<T>()
        {
            PollAttempts++;
            throw new InvalidOperationException("simulated database failure");
        }
    }

    [Fact]
    public async Task PollFailure_DoesNotTerminateHostLoop_RetriesNextInterval()
    {
        var db = new FailingShipmentDbContext("outbox-test-loop");
        var service = new OutboxPublisherService(
            db,
            new MessagePublisher(new FakeOutboxChannel(new PublishOutcome(true, 1))),
            NullLogger<OutboxPublisherService>.Instance);

        var execute = typeof(OutboxPublisherService)
            .GetMethod("ExecuteAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        using var cts = new CancellationTokenSource();
        var executeTask = (Task)execute.Invoke(service, [cts.Token])!;

        // One failed poll, then the 2 s delay, then a second (failed) poll —
        // the loop must still be alive after a database failure.
        await Task.Delay(2400);
        Assert.True(db.PollAttempts >= 2, $"expected at least 2 poll attempts after a failure, got {db.PollAttempts}");

        cts.Cancel();
        var completion = await Task.WhenAny(executeTask, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(executeTask, completion);
        try
        {
            await executeTask;
        }
        catch (OperationCanceledException)
        {
            // Cancellation of the shutdown delay — graceful.
        }

        var attemptsAtShutdown = db.PollAttempts;
        await Task.Delay(500);
        Assert.Equal(attemptsAtShutdown, db.PollAttempts); // loop stopped, no runaway
    }
}
