using System.Text.Json;
using System.Text.Json.Serialization;
using KargoNet.Contracts.Events;
using KargoNet.Contracts.Models;
using KargoNet.Messaging.Consuming;
using Microsoft.EntityFrameworkCore.Update;
using KargoNet.Messaging.Infra;
using KargoNet.Messaging.Topology;
using KargoNet.Tracking.Api.Consuming;
using KargoNet.Tracking.Api.Data;
using KargoNet.Tracking.Api.Entities;
using KargoNet.Tracking.Api.Tracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace KargoNet.Tracking.Tests.Consuming
{
    public class ShipmentEventsConsumerTests
    {
        private sealed class RecordingCache : ITrackingCache
        {
            public List<string> Deletions { get; } = [];

            public Task<TrackingUpdatedDto?> GetAsync(string trackingNumber, CancellationToken ct)
                => Task.FromResult<TrackingUpdatedDto?>(null);

            public Task SetAsync(TrackingUpdatedDto dto, TimeSpan ttl, CancellationToken ct)
                => Task.CompletedTask;

            public Task DeleteAsync(string trackingNumber, CancellationToken ct)
            {
                Deletions.Add(trackingNumber);
                return Task.CompletedTask;
            }
        }

        private sealed class RecordingBroadcaster : IShippingUpdateBroadcaster
        {
            public List<TrackingUpdatedDto> Broadcasts { get; } = [];

            public Task BroadcastAsync(TrackingUpdatedDto dto, CancellationToken ct)
            {
                Broadcasts.Add(dto);
                return Task.CompletedTask;
            }
        }

        private sealed class Fixture : IDisposable
        {
            public TrackingDbContext Db { get; }
            public RecordingCache Cache { get; }
            public RecordingBroadcaster Broadcaster { get; }
            public FakeChannel Channel { get; }
            public ShipmentEventsConsumer Consumer { get; }
            public ShipmentEventHandler Handler { get; }

            public Fixture()
            {
                Db = CreateContext();
                Cache = new RecordingCache();
                Broadcaster = new RecordingBroadcaster();
                Channel = new FakeChannel();
                Handler = new ShipmentEventHandler(Db, Cache, Broadcaster, NullLogger<ShipmentEventHandler>.Instance);

                var provider = new ServiceCollection()
                    .AddSingleton<ShipmentEventHandler>(Handler)
                    .BuildServiceProvider();

                var broker = new RabbitConsumerBroker(new FakeChannelFactory(Channel), NullLogger<RabbitConsumerBroker>.Instance);
                Consumer = new ShipmentEventsConsumer(
                    provider.GetRequiredService<IServiceScopeFactory>(), broker, new FakeChannelFactory(Channel),
                    NullLogger<ShipmentEventsConsumer>.Instance);
            }

            public void Dispose() => Db.Dispose();
        }

        private static TrackingDbContext CreateContext() => new(CreateOptions());

        private static readonly JsonSerializerOptions WireOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
        };

        private static byte[] CreatedBody(Guid eventId, string trackingNumber)
        {
            var envelope = new EventEnvelope<ShipmentCreatedPayload>(
                eventId,
                ShipmentEventsConsumer.CreatedEventType,
                DateTimeOffset.UtcNow,
                JsonSerializer.SerializeToElement(new ShipmentCreatedPayload(trackingNumber, "Fragile", "Izmir"), WireOptions));
            return EventSerializer.Serialize(envelope);
        }

        private static byte[] StatusChangedBody(Guid eventId, string trackingNumber, ShipmentStatus toStatus)
        {
            var envelope = new EventEnvelope<ShipmentStatusChangedPayload>(
                eventId,
                ShipmentEventsConsumer.StatusChangedEventType,
                DateTimeOffset.UtcNow,
                JsonSerializer.SerializeToElement(new ShipmentStatusChangedPayload(
                    Guid.NewGuid(), trackingNumber, ShipmentStatus.Created, toStatus), WireOptions));
            return EventSerializer.Serialize(envelope);
        }

        [Fact]
        public async Task Created_body_applies_read_model_timeline_and_marker_and_side_effects()
        {
            using var fixture = new Fixture();
            var eventId = Guid.NewGuid();
            var body = CreatedBody(eventId, "TRK-100");

            await fixture.Consumer.HandleDeliveryAsync(7, body, null);

            var row = await fixture.Db.ShipmentTrackings.SingleAsync(t => t.TrackingNumber == "TRK-100");
            Assert.Equal("Created", row.Status);
            Assert.Equal("Fragile", row.Description);
            Assert.Equal("Izmir", row.Destination);

            var timeline = await fixture.Db.TrackingEvents.Where(e => e.EventId == eventId).SingleAsync();
            Assert.Equal(ShipmentEventsConsumer.CreatedEventType, timeline.EventType);
            Assert.Equal("TRK-100", timeline.TrackingNumber);

            Assert.True(await fixture.Db.ProcessedEvents.AnyAsync(p => p.EventId == eventId));

            Assert.Equal([7UL], fixture.Channel.Acks);
            Assert.Empty(fixture.Channel.Publishes);
            Assert.Equal(["TRK-100"], fixture.Cache.Deletions);
            var broadcast = Assert.Single(fixture.Broadcaster.Broadcasts);
            Assert.Equal("TRK-100", broadcast.trackingNumber);
            Assert.Equal("Created", broadcast.status);
        }

        [Fact]
        public async Task Redelivered_same_event_id_is_applied_exactly_once_and_ackd()
        {
            using var fixture = new Fixture();
            var eventId = Guid.NewGuid();
            var body = CreatedBody(eventId, "TRK-101");

            await fixture.Consumer.HandleDeliveryAsync(1, body, null);
            await fixture.Consumer.HandleDeliveryAsync(2, body, null);

            Assert.Equal(1, await fixture.Db.ShipmentTrackings.CountAsync());
            Assert.Equal(1, await fixture.Db.TrackingEvents.CountAsync());
            Assert.Equal(1, await fixture.Db.ProcessedEvents.CountAsync());
            Assert.Equal([1UL, 2UL], fixture.Channel.Acks);
            Assert.Empty(fixture.Channel.Publishes);
            Assert.Single(fixture.Cache.Deletions);
            Assert.Single(fixture.Broadcaster.Broadcasts);
        }

        [Fact]
        public async Task Race_backstop_committed_marker_is_ackd_as_success_without_retry()
        {
            // The losing writer of a duplicate race sees its SaveChanges fail
            // with a DbUpdateException AFTER a rival marker commits: the D2
            // backstop must treat that as "already processed" and ack.
            using var db = new RaceSimulatingTrackingDbContext(CreateOptions(), true);
            var cache = new RecordingCache();
            var broadcaster = new RecordingBroadcaster();
            var handler = new ShipmentEventHandler(db, cache, broadcaster, NullLogger<ShipmentEventHandler>.Instance);
            var provider = new ServiceCollection()
                .AddSingleton<ShipmentEventHandler>(handler)
                .BuildServiceProvider();

            var channel = new FakeChannel();
            var consumer = new ShipmentEventsConsumer(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new RabbitConsumerBroker(new FakeChannelFactory(channel), NullLogger<RabbitConsumerBroker>.Instance),
                new FakeChannelFactory(channel),
                NullLogger<ShipmentEventsConsumer>.Instance);

            var eventId = Guid.NewGuid();
            await consumer.HandleDeliveryAsync(9, CreatedBody(eventId, "TRK-200"), null);
            Assert.Equal([9UL], channel.Acks);
            Assert.Empty(channel.Publishes);
            Assert.True(await db.ProcessedEvents.AnyAsync(e => e.EventId == eventId));
            Assert.Equal(0, await db.ShipmentTrackings.CountAsync());
            // The swallow path still runs the idempotent post-commit side effects.
            Assert.Single(broadcaster.Broadcasts);
        }

        [Fact]
        public async Task DbUpdateException_without_committed_marker_is_rethrown_and_routed_to_retry()
        {
            using var db = new RaceSimulatingTrackingDbContext(CreateOptions(), false);
            var handler = new ShipmentEventHandler(db, new RecordingCache(), new RecordingBroadcaster(), NullLogger<ShipmentEventHandler>.Instance);
            var provider = new ServiceCollection()
                .AddSingleton<ShipmentEventHandler>(handler)
                .BuildServiceProvider();

            var channel = new FakeChannel();
            var consumer = new ShipmentEventsConsumer(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new RabbitConsumerBroker(new FakeChannelFactory(channel), NullLogger<RabbitConsumerBroker>.Instance),
                new FakeChannelFactory(channel),
                NullLogger<ShipmentEventsConsumer>.Instance);

            var eventId = Guid.NewGuid();
            var occurredAt = DateTimeOffset.UtcNow;
            var payload = new ShipmentCreatedPayload("TRK-201", "Fragile", "Izmir");

            // A genuine write failure (no rival marker committed) must
            // surface as DbUpdateException from the handler.
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                handler.ApplyCreatedAsync(eventId, ShipmentEventsConsumer.CreatedEventType, occurredAt, JsonSerializer.Serialize(payload), payload));

            // End-to-end: the failure routes to the retry queue and is acked.
            await consumer.HandleDeliveryAsync(3, CreatedBody(eventId, "TRK-201"), null);
            var first = Assert.Single(channel.Publishes);
            Assert.Equal(RabbitTopology.RetryRoutingKey, first.RoutingKey);
            Assert.Equal(1, first.Headers!["x-retry-count"]);
            Assert.Equal([3UL], channel.Acks);
        }

        [Fact]
        public async Task Status_changed_body_updates_status_and_appends_timeline_row()
        {
            using var fixture = new Fixture();
            var createdId = Guid.NewGuid();
            var changedId = Guid.NewGuid();

            await fixture.Consumer.HandleDeliveryAsync(1, CreatedBody(createdId, "TRK-102"), null);
            await fixture.Consumer.HandleDeliveryAsync(2, StatusChangedBody(changedId, "TRK-102", ShipmentStatus.PickedUp), null);

            var row = await fixture.Db.ShipmentTrackings.SingleAsync(t => t.TrackingNumber == "TRK-102");
            Assert.Equal(ShipmentStatus.PickedUp.ToString(), row.Status);

            var timeline = await fixture.Db.TrackingEvents
                .OrderBy(e => e.OccurredAt)
                .ToListAsync();
            Assert.Equal(2, timeline.Count);
            Assert.Equal(ShipmentEventsConsumer.StatusChangedEventType, timeline[1].EventType);

            Assert.Equal([1UL, 2UL], fixture.Channel.Acks);
            Assert.Empty(fixture.Channel.Publishes);
            Assert.Equal(2, fixture.Cache.Deletions.Count);
            Assert.Equal(2, fixture.Broadcaster.Broadcasts.Count);
        }

        [Fact]
        public async Task Unknown_event_type_routes_to_retry_then_dlq_and_always_ackd()
        {
            using var fixture = new Fixture();
            var envelope = new EventEnvelope<JsonElement?>(
                Guid.NewGuid(), "shipment.exploded", DateTimeOffset.UtcNow, null);
            var body = EventSerializer.Serialize(envelope);

            // First failure: no retry header yet -> republish to the retry queue.
            await fixture.Consumer.HandleDeliveryAsync(1, body, null);
            var first = Assert.Single(fixture.Channel.Publishes);
            Assert.Equal(RabbitTopology.Exchange, first.Exchange);
            Assert.Equal(RabbitTopology.RetryRoutingKey, first.RoutingKey);
            Assert.Equal(1, first.Headers!["x-retry-count"]);
            Assert.False(string.IsNullOrEmpty((string)first.Headers!["x-exception"]!));
            Assert.Equal([1UL], fixture.Channel.Acks);

            // Exhausted retries (header at the cap) -> dead-letter, still acked.
            var exhausted = new Dictionary<string, object?> { ["x-retry-count"] = RetryPolicy.MaxRetries };
            await fixture.Consumer.HandleDeliveryAsync(2, body, exhausted);
            var second = fixture.Channel.Publishes[1];
            Assert.Equal(RabbitTopology.DlqRoutingKey, second.RoutingKey);
            Assert.Equal([1UL, 2UL], fixture.Channel.Acks);
        }

        [Fact]
        public async Task Handler_failure_routes_to_retry_then_dlq_and_always_ackd()
        {
            using var fixture = new Fixture();
            // A payload that is not a JSON object makes the typed
            // deserialization inside the handler throw.
            var envelope = new EventEnvelope<JsonElement?>(
                Guid.NewGuid(),
                ShipmentEventsConsumer.StatusChangedEventType,
                DateTimeOffset.UtcNow,
                JsonSerializer.SerializeToElement("not-a-payload"));
            var body = EventSerializer.Serialize(envelope);

            await fixture.Consumer.HandleDeliveryAsync(1, body, null);
            var first = Assert.Single(fixture.Channel.Publishes);
            Assert.Equal(RabbitTopology.RetryRoutingKey, first.RoutingKey);
            Assert.Equal(1, first.Headers!["x-retry-count"]);
            Assert.Equal([1UL], fixture.Channel.Acks);

            var exhausted = new Dictionary<string, object?> { ["x-retry-count"] = RetryPolicy.MaxRetries };
            await fixture.Consumer.HandleDeliveryAsync(2, body, exhausted);
            Assert.Equal(RabbitTopology.DlqRoutingKey, fixture.Channel.Publishes[1].RoutingKey);
            Assert.Equal([1UL, 2UL], fixture.Channel.Acks);
            Assert.Equal(0, await fixture.Db.ShipmentTrackings.CountAsync());
        }

        [Fact]
        public async Task Race_backstop_duplicate_processed_marker_returns_success_without_rethrowing()
        {
            using var fixture = new Fixture();
            var eventId = Guid.NewGuid();

            // Pre-seed the ProcessedEvent PK: in a true race the loser's insert
            // collides and the DbUpdateException path treats it as processed.
            // InMemory cannot force that collision deterministically, so this
            // exercises the same "already processed -> success" contract via
            // the deterministic skip path.
            fixture.Db.ProcessedEvents.Add(new ProcessedEvent
            {
                EventId = eventId,
                ProcessedAt = DateTimeOffset.UtcNow,
            });
            await fixture.Db.SaveChangesAsync();

            await fixture.Handler.ApplyCreatedAsync(
                eventId, "shipment.created", DateTimeOffset.UtcNow, "{}",
                new ShipmentCreatedPayload("TRK-104", "Fragile", "Izmir"));

            Assert.Equal(0, await fixture.Db.ShipmentTrackings.CountAsync());
            Assert.Equal(0, await fixture.Db.TrackingEvents.CountAsync());
            Assert.Empty(fixture.Cache.Deletions);
            Assert.Empty(fixture.Broadcaster.Broadcasts);
        }

        /// <summary><see cref="LazyChannelFactory"/> wired to a canned channel (no broker).</summary>
        private sealed class FakeChannelFactory : LazyChannelFactory
        {
            private readonly IChannel _channel;

            public FakeChannelFactory(IChannel channel)
                : base(new ConfigurationBuilder().Build())
                => _channel = channel;

            public override Task<IChannel> GetChannelAsync() => Task.FromResult(_channel);

            public override Task<IChannel> CreateChannelAsync() => Task.FromResult(_channel);
        }

        /// <summary>
        /// Hand-rolled <see cref="IChannel"/> covering the calls the
        /// <see cref="RabbitConsumerBroker"/> and
        /// <see cref="ShipmentEventsConsumer"/> pipeline make (acks,
        /// republishes); everything else throws.
        /// </summary>
        private sealed class FakeChannel : IChannel
        {
            public List<ulong> Acks { get; } = [];

            public bool Disposed { get; private set; }

            public sealed record Published(
                string Exchange,
                string RoutingKey,
                IReadOnlyBasicProperties? Properties,
                byte[] Body)
            {
                public IDictionary<string, object>? Headers => Properties?.Headers;
            }

            public List<Published> Publishes { get; } = [];

            public ValueTask BasicAckAsync(ulong deliveryTag, bool multiple, CancellationToken cancellationToken = default)
            {
                Acks.Add(deliveryTag);
                return ValueTask.CompletedTask;
            }

            public ValueTask BasicPublishAsync<TProperties>(string exchange, string routingKey, bool mandatory, TProperties? basicProperties, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
                where TProperties : IReadOnlyBasicProperties, IAmqpHeader
            {
                Publishes.Add(new Published(exchange, routingKey, basicProperties, body.ToArray()));
                return ValueTask.CompletedTask;
            }

            public Task<BasicGetResult> BasicGetAsync(string queue, bool autoAck, CancellationToken cancellationToken = default)
                => Task.FromException<BasicGetResult>(new NotImplementedException());

            public Task<QueueDeclareOk> QueueDeclarePassiveAsync(string queue, CancellationToken cancellationToken = default)
                => Task.FromException<QueueDeclareOk>(new NotImplementedException());

            public ValueTask BasicPublishAsync<TProperties>(CachedString exchange, CachedString routingKey, bool mandatory, TProperties? basicProperties, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
                where TProperties : IReadOnlyBasicProperties, IAmqpHeader
                => throw new NotImplementedException();

            // --- members the consumer pipeline never touches ---------------------------

            public ValueTask BasicRejectAsync(ulong deliveryTag, bool requeue, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public ValueTask BasicNackAsync(ulong deliveryTag, bool multiple, bool requeue, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task<string> BasicConsumeAsync(string queue, bool autoAck, string consumerTag, bool noLocal, bool exclusive, IDictionary<string, object?>? arguments, IAsyncBasicConsumer consumer, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task BasicCancelAsync(string consumerTag, bool noWait = false, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task BasicQosAsync(uint prefetchSize, ushort prefetchCount, bool global, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public ValueTask<ulong> GetNextPublishSequenceNumberAsync(CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task CloseAsync(ushort replyCode, string replyText, bool abort, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task CloseAsync(ShutdownEventArgs reason, bool abort)
                => throw new NotImplementedException();

            public Task CloseAsync(ShutdownEventArgs reason, bool abort, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task<uint> ConsumerCountAsync(string queue, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task<uint> MessageCountAsync(string queue, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task ExchangeDeclareAsync(string exchange, string type, bool durable, bool autoDelete, IDictionary<string, object?>? arguments = null, bool passive = false, bool noWait = false, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task ExchangeDeclarePassiveAsync(string exchange, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task ExchangeDeleteAsync(string exchange, bool ifUnused = false, bool noWait = false, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task ExchangeBindAsync(string destination, string source, string routingKey, IDictionary<string, object?>? arguments = null, bool noWait = false, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task ExchangeUnbindAsync(string destination, string source, string routingKey, IDictionary<string, object?>? arguments = null, bool noWait = false, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task<QueueDeclareOk> QueueDeclareAsync(string queue, bool durable, bool exclusive, bool autoDelete, IDictionary<string, object?>? arguments = null, bool passive = false, bool noWait = false, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();


            public Task<uint> QueueDeleteAsync(string queue, bool ifUnused, bool ifEmpty, bool noWait = false, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task<uint> QueuePurgeAsync(string queue, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task QueueBindAsync(string queue, string exchange, string routingKey, IDictionary<string, object?>? arguments = null, bool noWait = false, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task QueueUnbindAsync(string queue, string exchange, string routingKey, IDictionary<string, object?>? arguments = null, bool noWait = false, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task QueueUnbindAsync(string queue, string exchange, string routingKey, IDictionary<string, object?>? arguments, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task TxSelectAsync(CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task TxCommitAsync(CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public Task TxRollbackAsync(CancellationToken cancellationToken = default)
                => throw new NotImplementedException();

            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync()
            {
                Disposed = true;
                return ValueTask.CompletedTask;
            }

            // --- properties -----------------------------------------------------------

            public int ChannelNumber => 0;
            public ShutdownEventArgs? CloseReason => null;
            public IAsyncBasicConsumer? DefaultConsumer { get; set; }
            public bool IsClosed => false;
            public bool IsOpen => true;
            public string CurrentQueue => string.Empty;
            public TimeSpan ContinuationTimeout { get; set; }

            // --- events ---------------------------------------------------------------

            public event AsyncEventHandler<BasicAckEventArgs>? BasicAcksAsync
            {
                add => throw new NotImplementedException();
                remove => throw new NotImplementedException();
            }

            public event AsyncEventHandler<BasicNackEventArgs>? BasicNacksAsync
            {
                add => throw new NotImplementedException();
                remove => throw new NotImplementedException();
            }

            public event AsyncEventHandler<BasicReturnEventArgs>? BasicReturnAsync
            {
                add => throw new NotImplementedException();
                remove => throw new NotImplementedException();
            }

            public event AsyncEventHandler<CallbackExceptionEventArgs>? CallbackExceptionAsync
            {
                add => throw new NotImplementedException();
                remove => throw new NotImplementedException();
            }

            public event AsyncEventHandler<ShutdownEventArgs>? ChannelShutdownAsync
            {
                add => throw new NotImplementedException();
                remove => throw new NotImplementedException();
            }

            public event AsyncEventHandler<FlowControlEventArgs>? FlowControlAsync
            {
                add => throw new NotImplementedException();
                remove => throw new NotImplementedException();
            }
        }
    private static DbContextOptions<TrackingDbContext> CreateOptions()
    {
        var builder = new DbContextOptionsBuilder<TrackingDbContext>();
        builder.UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
        return builder.Options;
    }

    /// <summary>
    /// Simulates a failing <see cref="SaveChangesAsync"/> with a
    /// <see cref="DbUpdateException"/>. With <paramref name="commitRivalMarker"/>
    /// it first commits a rival <see cref="ProcessedEvent"/> marker (the
    /// concurrent-winner race the D2 backstop recognizes); without it, the
    /// marker stays absent (a genuine write failure).
    /// </summary>
    private sealed class RaceSimulatingTrackingDbContext : TrackingDbContext
    {
        private readonly DbContextOptions<TrackingDbContext> _options;
        private readonly bool _commitRivalMarker;
        private bool _simulated;

        public RaceSimulatingTrackingDbContext(
            DbContextOptions<TrackingDbContext> options,
            bool commitRivalMarker)
            : base(options)
        {
            _options = options;
            _commitRivalMarker = commitRivalMarker;
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!_simulated)
            {
                _simulated = true;

                // As the concurrent winner would, commit the marker first.
                if (_commitRivalMarker)
                {
                    var marker = ChangeTracker.Entries<ProcessedEvent>()
                        .FirstOrDefault(e => e.State == EntityState.Added);
                    if (marker is not null)
                    {
                        using var rival = new TrackingDbContext(_options);
                        rival.ProcessedEvents.Add(new ProcessedEvent
                        {
                            EventId = marker.Entity.EventId,
                            ProcessedAt = DateTimeOffset.UtcNow,
                        });
                        rival.SaveChanges();
                    }
                }
            }

            throw new DbUpdateException(
                "Duplicate key value violates unique constraint on ProcessedEvents.",
                new Exception("23505"));
        }
    }
    }
}
