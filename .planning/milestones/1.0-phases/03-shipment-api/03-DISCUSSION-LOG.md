# Phase 3 Discussion Log — Shipment API

**Date**: 2026-11-20
**Mode**: Autonomous smart discuss (full-autonomy run; no human prompts)

## Areas examined

### 1. Persistence shape
**Question**: What entities and indexes does the outbox pattern need?
**Decision**: `Shipment` (unique `TrackingNumber`, `Status` as string, audit timestamps) + `OutboxMessage` (`EventType`, `PayloadJson` = serialized envelope, `OutboxStatus`, `PublishedAt?`, index `(Status, CreatedAt)` for the publisher's pending scan).
**Rationale**: The publisher's hot path is `WHERE Status = Pending ORDER BY CreatedAt`; the index makes that an index scan. Storing the full serialized envelope in `PayloadJson` means the publisher publishes verbatim — no re-serialization drift between write time and publish time.

### 2. Event type names
**Question**: What `EventType` strings go in the envelope?
**Decision**: `shipment.created` and `shipment.status.changed`, both routed to `shipment` (main queue).
**Rationale**: Dot-lowercase matches the topic exchange naming (`kargonet.shipments`, `shipment.retry`); Phase 4's consumer switches on exactly these two strings.

### 3. API surface
**Question**: Which endpoints, and what goes in the transaction?
**Decision**: POST/GET/GET/{trackingNumber}/PUT/{id}/status as locked in 03-CONTEXT.md; the state mutation and its outbox row are one EF transaction on both write paths.
**Rationale**: SC1/SC3 require the shipment row and the event row to be atomic — a committed shipment without its event would violate the core value (no lost status propagation). 409 on duplicate tracking number (unique index), 404 on missing, no transition state machine (none defined in the plan).

### 4. Publisher semantics
**Question**: How does the BackgroundService poll, confirm, and handle failure?
**Decision**: 2 s interval, batch 50, per-row confirm via `MessagePublisher`, mark published on confirm, leave pending on any failure, never delete.
**Rationale**: The outbox is the durability mechanism for broker downtime — a failed publish must survive to the next poll. Per-row marking keeps a single unconfirmed row from blocking the batch's other rows on retry.

### 5. Testability without Docker
**Question**: How to verify EF + publisher behavior with no local Postgres/RabbitMQ?
**Decision**: `Microsoft.EntityFrameworkCore.InMemory` for unit tests; publisher's poll method driven directly with a fake `IOutboxChannel`; live compose e2e deferred to Phase 6.
**Rationale**: Same pattern as Phase 2 (broker-free verification by design; explicit Phase 6 deferral). InMemory supports `BeginTransaction` for the atomicity assertions.

## Open items carried forward
- Real broker + Postgres e2e: Phase 6 (Docker Compose).
- `ConnectionStrings:ShipmentDb` value: supplied by Phase 6's compose `.env`/env vars.
- SignalR/Redis/Tracking concerns: Phase 4.
