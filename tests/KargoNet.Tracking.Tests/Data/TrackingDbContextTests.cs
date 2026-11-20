using KargoNet.Tracking.Api.Data;
using KargoNet.Tracking.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace KargoNet.Tracking.Tests.Data
{
    public class TrackingDbContextTests
    {
        private static TrackingDbContext CreateContext(string databaseName)
        {
            var options = new DbContextOptionsBuilder<TrackingDbContext>()
                .UseInMemoryDatabase(databaseName)
                .Options;

            return new TrackingDbContext(options);
        }

        [Fact]
        public void Model_shape_carries_three_pks_and_the_tracking_event_composite_index()
        {
            using var context = CreateContext(Guid.NewGuid().ToString("N"));
            var model = context.Model;

            var tracking = model.FindEntityType(typeof(ShipmentTracking))!;
            Assert.Equal([nameof(ShipmentTracking.TrackingNumber)],
                tracking.GetProperties().Where(p => p.IsPrimaryKey()).Select(p => p.Name).ToArray());

            var trackingEvent = model.FindEntityType(typeof(TrackingEvent))!;
            Assert.Equal([nameof(TrackingEvent.EventId)],
                trackingEvent.GetProperties().Where(p => p.IsPrimaryKey()).Select(p => p.Name).ToArray());
            var compositeIndex = trackingEvent.GetIndexes()
                .Where(i => i.Properties.Count == 2
                            && i.Properties[0].Name == nameof(TrackingEvent.TrackingNumber)
                            && i.Properties[1].Name == nameof(TrackingEvent.OccurredAt))
                .Single();
            Assert.Equal(2, compositeIndex.Properties.Count);

            var processed = model.FindEntityType(typeof(ProcessedEvent))!;
            Assert.Equal([nameof(ProcessedEvent.EventId)],
                processed.GetProperties().Where(p => p.IsPrimaryKey()).Select(p => p.Name).ToArray());
        }

        [Fact]
        public async Task Round_trip_smoke_inserts_and_queries_all_three_read_model_rows()
        {
            var databaseName = Guid.NewGuid().ToString("N");
            using var context = CreateContext(databaseName);

            var now = DateTimeOffset.UtcNow;
            var tracking = new ShipmentTracking
            {
                TrackingNumber = "TRK-200",
                Description = "Fragile",
                Destination = "Izmir",
                Status = "Created",
                CreatedAt = now,
                UpdatedAt = now,
            };
            var trackingEvent = new TrackingEvent
            {
                EventId = Guid.NewGuid(),
                TrackingNumber = "TRK-200",
                EventType = "shipment.created",
                PayloadJson = "{}",
                OccurredAt = now,
            };
            var processed = new ProcessedEvent
            {
                EventId = trackingEvent.EventId,
                ProcessedAt = now,
            };

            context.ShipmentTrackings.Add(tracking);
            context.TrackingEvents.Add(trackingEvent);
            context.ProcessedEvents.Add(processed);
            await context.SaveChangesAsync();

            var loadedTracking = await context.ShipmentTrackings.SingleAsync(t => t.TrackingNumber == "TRK-200");
            Assert.Equal("Fragile", loadedTracking.Description);
            Assert.Equal("Izmir", loadedTracking.Destination);
            Assert.Equal("Created", loadedTracking.Status);
            Assert.Equal(now, loadedTracking.CreatedAt);
            Assert.Equal(now, loadedTracking.UpdatedAt);

            var loadedEvent = await context.TrackingEvents.SingleAsync(e => e.EventId == trackingEvent.EventId);
            Assert.Equal("TRK-200", loadedEvent.TrackingNumber);
            Assert.Equal("shipment.created", loadedEvent.EventType);
            Assert.Equal("{}", loadedEvent.PayloadJson);
            Assert.Equal(now, loadedEvent.OccurredAt);

            var loadedProcessed = await context.ProcessedEvents.SingleAsync(p => p.EventId == processed.EventId);
            Assert.Equal(now, loadedProcessed.ProcessedAt);
        }
    }
}
