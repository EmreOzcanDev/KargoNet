using System.Text.Json;
using System.Text.Json.Serialization;
using KargoNet.Contracts.Events;
using KargoNet.Contracts.Models;
using KargoNet.Messaging.Consuming;
using KargoNet.Messaging.Topology;
using KargoNet.Tracking.Api.Consuming;
using KargoNet.Tracking.Api.Data;
using KargoNet.Tracking.Api.Entities;
using KargoNet.Tracking.Api.Tracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace KargoNet.Tracking.Tests.Consuming
{
    public class ShipmentEventsConsumerTests
    {
        private sealed class RecordingBroker : IConsumerBroker
        {
            public List<ulong> Acks { get; } = [];

            public record Publish(string Exchange, string RoutingKey, byte[] Body, IDictionary<string, object?>? Headers);

            public List<Publish> Publishes { get; } = [];

            public Exception? PublishFailure = null;

            public Task AckAsync(ulong deliveryTag)
            {
                Acks.Add(deliveryTag);
                return Task.CompletedTask;
            }

            public Task PublishAsync(string exchange, string routingKey, ReadOnlyMemory<byte> body, IDictionary<string, object?>? headers)
            {
                if (PublishFailure is not null)
                    throw PublishFailure;

                Publishes.Add(new Publish(exchange, routingKey, body.ToArray(), headers));
                return Task.CompletedTask;
            }
        }

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
            public RecordingBroker Broker { get; }
            public ShipmentEventsConsumer Consumer { get; }
            public ShipmentEventHandler Handler { get; }

            public Fixture()
            {
                Db = CreateContext();
                Cache = new RecordingCache();
                Broadcaster = new RecordingBroadcaster();
                Broker = new RecordingBroker();
                Handler = new ShipmentEventHandler(Db, Cache, Broadcaster, NullLogger<ShipmentEventHandler>.Instance);
                Consumer = new ShipmentEventsConsumer(Broker, Handler, NullLogger<ShipmentEventsConsumer>.Instance);
            }

            public void Dispose() => Db.Dispose();
        }

        private static TrackingDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<TrackingDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            return new TrackingDbContext(options);
        }

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

            Assert.Equal([7UL], fixture.Broker.Acks);
            Assert.Empty(fixture.Broker.Publishes);
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
            Assert.Equal([1UL, 2UL], fixture.Broker.Acks);
            Assert.Empty(fixture.Broker.Publishes);
            Assert.Single(fixture.Cache.Deletions);
            Assert.Single(fixture.Broadcaster.Broadcasts);
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

            Assert.Equal([1UL, 2UL], fixture.Broker.Acks);
            Assert.Empty(fixture.Broker.Publishes);
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
            var first = Assert.Single(fixture.Broker.Publishes);
            Assert.Equal(RabbitTopology.Exchange, first.Exchange);
            Assert.Equal(RabbitTopology.RetryRoutingKey, first.RoutingKey);
            Assert.Equal(1, first.Headers!["x-retry-count"]);
            Assert.False(string.IsNullOrEmpty((string)first.Headers!["x-exception"]!));
            Assert.Equal([1UL], fixture.Broker.Acks);

            // Exhausted retries (header at the cap) -> dead-letter, still acked.
            var exhausted = new Dictionary<string, object?> { ["x-retry-count"] = RetryPolicy.MaxRetries };
            await fixture.Consumer.HandleDeliveryAsync(2, body, exhausted);
            var second = fixture.Broker.Publishes[1];
            Assert.Equal(RabbitTopology.DlqRoutingKey, second.RoutingKey);
            Assert.Equal([1UL, 2UL], fixture.Broker.Acks);
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
            var first = Assert.Single(fixture.Broker.Publishes);
            Assert.Equal(RabbitTopology.RetryRoutingKey, first.RoutingKey);
            Assert.Equal(1, first.Headers!["x-retry-count"]);
            Assert.Equal([1UL], fixture.Broker.Acks);

            var exhausted = new Dictionary<string, object?> { ["x-retry-count"] = RetryPolicy.MaxRetries };
            await fixture.Consumer.HandleDeliveryAsync(2, body, exhausted);
            Assert.Equal(RabbitTopology.DlqRoutingKey, fixture.Broker.Publishes[1].RoutingKey);
            Assert.Equal([1UL, 2UL], fixture.Broker.Acks);
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
    }
}
