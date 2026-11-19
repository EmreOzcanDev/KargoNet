# Phase 3 Context — Shipment API

**Goal**: Shipments can be created, listed, and status-updated via REST, with events reliably published through the outbox pattern.
**Requirements**: SHIP-01, SHIP-02, SHIP-03, SHIP-04
**Depends on**: Phase 2 (KargoNet.Contracts events, KargoNet.Messaging publisher/topology)

## Locked decisions

### EF Core model (Npgsql provider)

`ShipmentDbContext` with two sets:

- **`Shipment`**: `Guid Id` (PK), `string TrackingNumber` (unique index, max 64), `string? Description`, `string Destination` (max 256), `ShipmentStatus Status` (default `Created`, stored as string), `DateTimeOffset CreatedAt`, `DateTimeOffset UpdatedAt`.
- **`OutboxMessage`**: `Guid Id` (PK), `string EventType` (max 64), `string PayloadJson` (the serialized `EventEnvelope` envelope as JSON), `OutboxStatus Status` (`Pending` | `Published`, stored as string), `DateTimeOffset CreatedAt`, `DateTimeOffset? PublishedAt`. Index on `(Status, CreatedAt)`.

Initial migration `InitialCreate` generated via `dotnet ef migrations add` with an `IDesignTimeDbContextFactory` (Npgsql connection string, no live DB needed at design time).

### Event types (pins the envelope `EventType` strings)

- `shipment.created` → `ShipmentCreatedPayload { trackingNumber, description, destination }`
- `shipment.status.changed` → `ShipmentStatusChangedPayload { shipmentId, trackingNumber, fromStatus, toStatus }`

Both use the Phase 2 `EventSerializer` (camelCase, enums by name). Both route to routing key `shipment` (main queue) — `RabbitTopology.MainRoutingKey`.

### REST endpoints (minimal API, camelCase JSON)

| Method | Route | Behavior |
|--------|-------|----------|
| POST | `/api/shipments` | Body `{ trackingNumber, description, destination }` → 201 + `ShipmentDto`; inserts `Shipment` (Status=Created) + `OutboxMessage(shipment.created)` in one EF transaction |
| GET | `/api/shipments` | 200 + list of `ShipmentDto` ordered by `CreatedAt` |
| GET | `/api/shipments/{trackingNumber}` | 200 + `ShipmentDto`, 404 when absent |
| PUT | `/api/shipments/{id}/status` | Body `{ status }` → 200 + updated `ShipmentDto`; updates `Status`/`UpdatedAt` + inserts `OutboxMessage(shipment.status.changed)` in one EF transaction; 404 when absent |

`ShipmentDto { id, trackingNumber, description, destination, status, createdAt, updatedAt }`. Tracking number trimmed; duplicate → 409. No status-transition state machine (any status accepted — the plan defines none).

### Outbox publisher (`OutboxPublisherService : BackgroundService`)

- Poll interval **2 s**, batch size **50**: `WHERE Status = Pending ORDER BY CreatedAt LIMIT 50`.
- Per row: `MessagePublisher.PublishAsync("shipment", row.PayloadJson bytes)` — the stored envelope JSON is published verbatim.
- On confirm: mark row `Published` + `PublishedAt` (own short transaction per row).
- On any failure (`PublishNotConfirmedException` or broker error): log, leave row `Pending` — retried on the next poll. Never delete on failure.
- Respects host shutdown (`StoppingToken`).

### DI wiring (Shipment.Api `Program.cs`)

- `AddDbContext<ShipmentDbContext>` (Npgsql from `ConnectionStrings:ShipmentDb`), `IOutboxChannel` → `RabbitOutboxChannel`, `MessagePublisher`, `AddHostedService<OutboxPublisherService>`.
- Real broker connection is constructed lazily/optionally: the API must start and serve REST + outbox writes without a reachable broker (publish failures stay in the outbox); the publisher channel is registered so Phase 6's compose stack provides the endpoint.

### Testing (xunit + `Microsoft.EntityFrameworkCore.InMemory`)

- InMemory provider for unit tests only (Npgsql stays the production provider); no live DB required.
- Coverage: create → shipment + `shipment.created` outbox row in one transaction; status update → `shipment.status.changed` outbox row with correct from/to; GET endpoints (list, by number, 404); publisher: pending row → published + marked; publish failure → row stays pending; publisher skips published rows.
- Publisher tested by driving its poll method directly (not the timer loop) against InMemory + a fake `IOutboxChannel`.

### Constraints carried in

- .NET 10, EF Core + Npgsql, RabbitMQ.Client 7.x (already pinned 7.2.2 in Messaging), hand-built outbox (no library).
- xunit 2.9.3 / runner 2.8.2 in tests/Tracking.Tests — never bump.
- No auth; no new solution layout — only `src/Services/Shipment.Api` + tests.
- Live-broker e2e (real publish over compose) remains a Phase 6 deferral; unit-testable halves are proven here.
