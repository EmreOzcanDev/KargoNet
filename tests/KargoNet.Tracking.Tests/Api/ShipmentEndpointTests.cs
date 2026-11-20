using System.Net;
using System.Net.Http.Json;
using System.Text;
using KargoNet.Contracts.Events;
using KargoNet.Contracts.Models;
using KargoNet.Shipment.Api.Data;
using KargoNet.Shipment.Api.Dto;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KargoNet.Tracking.Tests.Api
{
    using Shipment = KargoNet.Shipment.Api.Entities.Shipment;

    public class ShipmentApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName;

        public ShipmentApiFactory(string databaseName)
            => _databaseName = databaseName;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Unique environment per database name: the factory host cache is
            // keyed by environment, so each test gets an isolated host.
            builder.UseEnvironment($"testing-{_databaseName}");

            // Program.cs selects the database provider from configuration;
            // point the test host at an isolated InMemory database.
            builder.UseSetting("Database:Provider", "InMemory");
            builder.UseSetting("Database:Name", _databaseName);
        }

    }

    public class ShipmentEndpointTests
    {
        private readonly string _databaseName = Guid.NewGuid().ToString("N");
        private readonly ShipmentApiFactory _factory;

        public ShipmentEndpointTests()
            => _factory = new ShipmentApiFactory(_databaseName);

        private async Task<Shipment> CreateShipmentAsync(ShipmentDbContext db, string trackingNumber)
        {
            var shipment = new Shipment
            {
                TrackingNumber = trackingNumber,
                Description = "desc",
                Destination = "Izmir",
                Status = ShipmentStatus.Created,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            db.Shipments.Add(shipment);
            await db.SaveChangesAsync();
            return shipment;
        }

        [Fact]
        public async Task Create_shipment_persists_shipment_and_outbox_row_atomically()
        {
            var client = _factory.CreateClient();

            var response = await client.PostAsJsonAsync("/api/shipments", new CreateShipmentRequest("TRK-1", "desc", "Izmir"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<ShipmentDto>();
            Assert.NotNull(body);
            Assert.Equal("TRK-1", body!.TrackingNumber);
            Assert.Equal("Created", body.Status);

            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ShipmentDbContext>();

            var shipments = await db.Shipments.ToListAsync();
            Assert.Single(shipments);
            Assert.Equal("TRK-1", shipments[0].TrackingNumber);
            Assert.Equal(ShipmentStatus.Created, shipments[0].Status);

            var outboxRows = await db.OutboxMessages.ToListAsync();
            Assert.Single(outboxRows);
            Assert.Equal("shipment.created", outboxRows[0].EventType);

            var envelope = EventSerializer.Deserialize<ShipmentCreatedPayload>(
                Encoding.UTF8.GetBytes(outboxRows[0].PayloadJson));
            Assert.NotNull(envelope.Payload);
            var payload = envelope.Payload!.Value;
            Assert.Equal("TRK-1", payload.GetProperty("trackingNumber").GetString());
            Assert.Equal("desc", payload.GetProperty("description").GetString());
            Assert.Equal("Izmir", payload.GetProperty("destination").GetString());
        }

        [Fact]
        public async Task Create_shipment_with_duplicate_tracking_number_returns_409()
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ShipmentDbContext>();
            await CreateShipmentAsync(db, "TRK-DUP");

            var client = _factory.CreateClient();
            var response = await client.PostAsJsonAsync("/api/shipments", new CreateShipmentRequest("TRK-DUP", null, "Izmir"));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task Create_shipment_with_empty_or_whitespace_tracking_number_returns_400()
        {
            var client = _factory.CreateClient();

            var missing = await client.PostAsJsonAsync("/api/shipments", new CreateShipmentRequest(null, null, "Izmir"));
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

            var blank = await client.PostAsJsonAsync("/api/shipments", new CreateShipmentRequest("   ", null, "Izmir"));
            Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);

            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ShipmentDbContext>();
            Assert.Equal(0, await db.Shipments.CountAsync()); // nothing persisted
        }

        [Fact]
        public async Task Create_shipment_with_over_long_tracking_number_or_destination_returns_400()
        {
            var client = _factory.CreateClient();

            // varchar(64) / varchar(256): over-long input is a client error,
            // NOT a 409 unique-violation backstop.
            var longTrackingNumber = await client.PostAsJsonAsync(
                "/api/shipments",
                new CreateShipmentRequest(new string('X', 65), null, "Izmir"));
            Assert.Equal(HttpStatusCode.BadRequest, longTrackingNumber.StatusCode);

            var longDestination = await client.PostAsJsonAsync(
                "/api/shipments",
                new CreateShipmentRequest("TRK-LONG", null, new string('X', 257)));
            Assert.Equal(HttpStatusCode.BadRequest, longDestination.StatusCode);

            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ShipmentDbContext>();
            Assert.Equal(0, await db.Shipments.CountAsync()); // nothing persisted
        }

        [Fact]
        public async Task List_shipments_returns_shipments_ordered_by_created_at()
        {
            var client = _factory.CreateClient();

            var created = new[] { "TRK-L1", "TRK-L2", "TRK-L3" };
            foreach (var trackingNumber in created)
            {
                var response = await client.PostAsJsonAsync("/api/shipments", new CreateShipmentRequest(trackingNumber, null, "Izmir"));
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            }

            var list = await client.GetFromJsonAsync<List<ShipmentDto>>("/api/shipments");
            Assert.NotNull(list);
            Assert.Equal(created, list!.Select(s => s.TrackingNumber).ToArray());
        }

        [Fact]
        public async Task Get_by_tracking_number_returns_200_when_present_and_404_when_absent()
        {
            var client = _factory.CreateClient();

            await client.PostAsJsonAsync("/api/shipments", new CreateShipmentRequest("TRK-G1", null, "Izmir"));

            var found = await client.GetAsync("/api/shipments/TRK-G1");
            Assert.Equal(HttpStatusCode.OK, found.StatusCode);
            var body = await found.Content.ReadFromJsonAsync<ShipmentDto>();
            Assert.Equal("TRK-G1", body!.TrackingNumber);

            var missing = await client.GetAsync("/api/shipments/TRK-NONE");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        [Fact]
        public async Task Update_status_persists_status_and_status_changed_outbox_row()
        {
            var client = _factory.CreateClient();

            var created = await client.PostAsJsonAsync("/api/shipments", new CreateShipmentRequest("TRK-U1", null, "Izmir"));
            var shipment = await created.Content.ReadFromJsonAsync<ShipmentDto>();
            Assert.NotNull(shipment);

            var updated = await client.PutAsJsonAsync($"/api/shipments/{shipment!.Id}/status", new UpdateStatusRequest("InTransit"));
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

            var body = await updated.Content.ReadFromJsonAsync<ShipmentDto>();
            Assert.Equal("InTransit", body!.Status);

            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ShipmentDbContext>();

            var loaded = await db.Shipments.SingleAsync(s => s.Id == shipment.Id);
            Assert.Equal(ShipmentStatus.InTransit, loaded.Status);

            var outboxRows = await db.OutboxMessages
                .Where(o => o.EventType == "shipment.status.changed")
                .ToListAsync();
            Assert.Single(outboxRows);

            var envelope = EventSerializer.Deserialize<ShipmentStatusChangedPayload>(
                Encoding.UTF8.GetBytes(outboxRows[0].PayloadJson));
            var payload = envelope.Payload!.Value;
            Assert.Equal(shipment.Id, payload.GetProperty("shipmentId").GetGuid());
            Assert.Equal("TRK-U1", payload.GetProperty("trackingNumber").GetString());
            Assert.Equal("Created", payload.GetProperty("fromStatus").GetString());
            Assert.Equal("InTransit", payload.GetProperty("toStatus").GetString());
        }

        [Fact]
        public async Task Update_status_with_missing_shipment_returns_404()
        {
            var client = _factory.CreateClient();

            var response = await client.PutAsJsonAsync($"/api/shipments/{Guid.NewGuid()}/status", new UpdateStatusRequest("InTransit"));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
