using KargoNet.Contracts.Models;
using KargoNet.Shipment.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace KargoNet.Tracking.Tests.Data
{
    using Shipment = KargoNet.Shipment.Api.Entities.Shipment;
    using OutboxMessage = KargoNet.Shipment.Api.Entities.OutboxMessage;
    using OutboxStatus = KargoNet.Shipment.Api.Entities.OutboxStatus;

    public class ShipmentDbContextTests
    {
        private static ShipmentDbContext CreateContext(string databaseName)
        {
            var options = new DbContextOptionsBuilder<ShipmentDbContext>()
                .UseInMemoryDatabase(databaseName)
                .Options;

            return new ShipmentDbContext(options);
        }

        [Fact]
        public void Model_shape_carries_locked_indexes_and_string_backed_statuses()
        {
            using var context = CreateContext(Guid.NewGuid().ToString("N"));
            var model = context.Model;

            var shipment = model.FindEntityType(typeof(Shipment))!;
            var trackingNumberIndex = shipment.GetIndexes()
                .Where(i => i.Properties.Count == 1 && i.Properties.Single().Name == nameof(Shipment.TrackingNumber))
                .Single();
            Assert.True(trackingNumberIndex.IsUnique);

            var shipmentStatus = shipment.FindProperty(nameof(Shipment.Status))!;
            Assert.Equal(typeof(string), shipmentStatus.GetProviderClrType());

            var outbox = model.FindEntityType(typeof(OutboxMessage))!;
            var outboxIndex = outbox.GetIndexes()
                .Where(i => i.Properties.Count == 2
                            && i.Properties[0].Name == nameof(OutboxMessage.Status)
                            && i.Properties[1].Name == nameof(OutboxMessage.CreatedAt))
                .Single();
            Assert.Equal(2, outboxIndex.Properties.Count);

            var outboxStatus = outbox.FindProperty(nameof(OutboxMessage.Status))!;
            Assert.Equal(typeof(string), outboxStatus.GetProviderClrType());
        }

        [Fact]
        public async Task Round_trip_smoke_inserts_and_queries_shipment_and_outbox_row()
        {
            var databaseName = Guid.NewGuid().ToString("N");
            using var context = CreateContext(databaseName);

            var now = DateTimeOffset.UtcNow;
            var shipment = new Shipment
            {
                TrackingNumber = "TRK-100",
                Description = "Fragile",
                Destination = "Izmir",
                CreatedAt = now,
                UpdatedAt = now,
            };

            var outbox = new OutboxMessage
            {
                EventType = "shipment.created",
                PayloadJson = "{}",
                CreatedAt = now,
            };

            context.Shipments.Add(shipment);
            context.OutboxMessages.Add(outbox);
            await context.SaveChangesAsync();

            var loadedShipment = await context.Shipments.SingleAsync(s => s.Id == shipment.Id);
            Assert.Equal(ShipmentStatus.Created, loadedShipment.Status);
            Assert.Equal("TRK-100", loadedShipment.TrackingNumber);
            Assert.Equal("Fragile", loadedShipment.Description);
            Assert.Equal("Izmir", loadedShipment.Destination);
            Assert.Equal(now, loadedShipment.CreatedAt);
            Assert.Equal(now, loadedShipment.UpdatedAt);

            var loadedOutbox = await context.OutboxMessages.SingleAsync(o => o.Id == outbox.Id);
            Assert.Equal(OutboxStatus.Pending, loadedOutbox.Status);
            Assert.Equal("shipment.created", loadedOutbox.EventType);
            Assert.Equal("{}", loadedOutbox.PayloadJson);
            Assert.Null(loadedOutbox.PublishedAt);
        }
    }
}
