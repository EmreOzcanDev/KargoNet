using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KargoNet.Contracts.Events;
using KargoNet.Contracts.Models;
using KargoNet.Shipment.Api.Data;
using KargoNet.Shipment.Api.Dto;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KargoNet.Shipment.Api.Endpoints
{
    using Shipment = KargoNet.Shipment.Api.Entities.Shipment;
    using OutboxMessage = KargoNet.Shipment.Api.Entities.OutboxMessage;
    using OutboxStatus = KargoNet.Shipment.Api.Entities.OutboxStatus;

    public static class Shipments
    {
        // Must match EventSerializer's options (camelCase, enums by name) so the
        // nested payload element round-trips through EventSerializer.Deserialize.
        private static readonly JsonSerializerOptions PayloadOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
        };
        public static IEndpointRouteBuilder MapShipmentEndpoints(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/shipments", CreateShipment);
            app.MapGet("/api/shipments", ListShipments);
            app.MapGet("/api/shipments/{trackingNumber}", GetShipmentByTrackingNumber);
            app.MapPut("/api/shipments/{id}/status", UpdateShipmentStatus);

            return app;
        }

        private static async Task<IResult> CreateShipment(CreateShipmentRequest request, ShipmentDbContext db)
        {
            var trackingNumber = request.TrackingNumber?.Trim() ?? string.Empty;
            var description = request.Description;
            var destination = request.Destination?.Trim() ?? string.Empty;

            // Column lengths (varchar(64) / varchar(256)): over-long input is a
            // client error (400), not a unique-violation backstop (409).
            if (trackingNumber.Length == 0 || trackingNumber.Length > 64 || destination.Length > 256)
                return Results.BadRequest();

            // InMemory tests cannot rely on the unique index; the pre-check is
            // the testable 409 path. A DbUpdateException on save stays as the
            // race-case backstop against the real Npgsql unique index.
            var duplicate = await db.Shipments.AnyAsync(s => s.TrackingNumber == trackingNumber);
            if (duplicate)
                return Results.Conflict();

            var now = DateTimeOffset.UtcNow;
            var payload = new ShipmentCreatedPayload(trackingNumber, description ?? string.Empty, destination);
            var envelope = new EventEnvelope<ShipmentCreatedPayload>(
                Guid.NewGuid(),
                "shipment.created",
                now,
                JsonSerializer.SerializeToElement(payload, PayloadOptions));

            var shipment = new Shipment
            {
                TrackingNumber = trackingNumber,
                Description = description,
                Destination = destination,
                Status = ShipmentStatus.Created,
                CreatedAt = now,
                UpdatedAt = now,
            };

            var outbox = new OutboxMessage
            {
                EventType = "shipment.created",
                PayloadJson = Encoding.UTF8.GetString(EventSerializer.Serialize(envelope)),
                Status = OutboxStatus.Pending,
                CreatedAt = now,
            };

            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                db.Shipments.Add(shipment);
                db.OutboxMessages.Add(outbox);
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            {
                // Unique-constraint violation only (SqlState 23505). Any other
                // database failure is not a duplicate — let it surface.
                return Results.Conflict();
            }

            return Results.Created("/api/shipments", ShipmentDto.From(shipment));
        }

        private static async Task<IResult> ListShipments(ShipmentDbContext db)
        {
            var shipments = await db.Shipments.AsNoTracking().OrderBy(s => s.CreatedAt).ToListAsync();
            return Results.Ok(shipments.Select(ShipmentDto.From).ToList());
        }

        private static async Task<IResult> GetShipmentByTrackingNumber(string trackingNumber, ShipmentDbContext db)
        {
            var shipment = await db.Shipments.AsNoTracking()
                .Where(s => s.TrackingNumber == trackingNumber)
                .FirstOrDefaultAsync();

            return shipment is null
                ? Results.NotFound()
                : Results.Ok(ShipmentDto.From(shipment));
        }

        private static async Task<IResult> UpdateShipmentStatus(Guid id, UpdateStatusRequest request, ShipmentDbContext db)
        {
            if (request.Status is not { } statusName
                || !Enum.TryParse<ShipmentStatus>(statusName, ignoreCase: true, out var toStatus))
                return Results.BadRequest();

            var shipment = await db.Shipments.FirstOrDefaultAsync(s => s.Id == id);
            if (shipment is null)
                return Results.NotFound();

            var fromStatus = shipment.Status;
            var now = DateTimeOffset.UtcNow;

            var payload = new ShipmentStatusChangedPayload(shipment.Id, shipment.TrackingNumber, fromStatus, toStatus);
            var envelope = new EventEnvelope<ShipmentStatusChangedPayload>(
                Guid.NewGuid(),
                "shipment.status.changed",
                now,
                JsonSerializer.SerializeToElement(payload, PayloadOptions));

            var outbox = new OutboxMessage
            {
                EventType = "shipment.status.changed",
                PayloadJson = Encoding.UTF8.GetString(EventSerializer.Serialize(envelope)),
                Status = OutboxStatus.Pending,
                CreatedAt = now,
            };

            shipment.Status = toStatus;
            shipment.UpdatedAt = now;

            await using var transaction = await db.Database.BeginTransactionAsync();
            db.OutboxMessages.Add(outbox);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            return Results.Ok(ShipmentDto.From(shipment));
        }
    }
}
