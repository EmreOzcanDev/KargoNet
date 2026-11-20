# Phase 4 Context — Tracking API

**Goal:** Build `Tracking.Api`: a consumer that applies shipment events to a read model with idempotency, a `GET /api/tracking/{trackingNumber}` endpoint with Redis cache-aside, a SignalR hub broadcasting live updates, and DLQ admin endpoints.

**Requirements:** TRK-01 (idempotent consumer, ProcessedEvents in same transaction), TRK-02 (cache-aside 10-min TTL, invalidated on event), TRK-03 (SignalR `ShipmentUpdated` to all + per-tracking-number group), TRK-04 (`GET /api/admin/dlq` count), TRK-05 (`POST /api/admin/dlq/replay`).

**Success criteria (ROADMAP):**
- SC1: Consumer processes `shipment.created` / `shipment.status.changed` into `ShipmentTracking` + `TrackingEvents`; a redelivered EventId is applied exactly once (ProcessedEvents check in the same EF transaction as the read-model update).
- SC2: `GET /api/tracking/{trackingNumber}` returns tracking data from Redis on hit, falls back to EF and repopulates the cache (10-min TTL); a new event deletes the cached entry.
- SC3: After each applied event, `ShipmentUpdated` is broadcast to all SignalR clients and to the group named after the tracking number.
- SC4: `GET /api/admin/dlq` returns the DLQ message count; `POST /api/admin/dlq/replay` moves DLQ messages back to the main queue and reports the count.

## Locked design

### Read model (`TrackingDbContext`, new `InitialCreate` migration)
- `ShipmentTracking { string TrackingNumber (PK), string Description, string Destination, string Status (default Created), DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt }` — string status, same convention as Shipment.Api.
- `TrackingEvent { Guid EventId (PK), string TrackingNumber, string EventType (max 64), string PayloadJson, DateTimeOffset OccurredAt }` index `(TrackingNumber, OccurredAt)` — timeline data for Phases 5.
- `ProcessedEvent { Guid EventId (PK), DateTimeOffset ProcessedAt }` — idempotency marker.
- `IDesignTimeDbContextFactory<TrackingDbContext>`; InMemory test switch via the same `Database:Provider`/`Database:Name` config keys as Shipment.Api (DI-swap routes are dead on EF Core 10 — Phase 3 finding).

### Consumer (`ShipmentEventsConsumer`, hosted service in Tracking.Api)
- Owns the delivery pipeline over Phase 2's `IConsumerBroker` seam (ack on success; on failure `RetryPolicy.Decide` → republish to `RabbitTopology.RetryRoutingKey` with `x-retry-count`+`x-exception`, or DLQ via `RabbitTopology.DlqRoutingKey`; **always ack**). It does not subclass `ConsumerBase<TPayload>` — that seam is single-type and Phase 4 carries two event types; the pipeline mirrors `ConsumerBase`'s contract exactly (same headers, same decision order) and stays testable with the Phase 2 fake broker.
- Deserializes `EventEnvelope<JsonElement?>` (envelope `Payload` is `JsonElement?`), switches on `EventType` — exactly `shipment.created` / `shipment.status.changed` (Phase 3's event types) — and deserializes the payload element into `ShipmentCreatedPayload` / `ShipmentStatusChangedPayload`. Unknown event type → handler failure → normal retry/DLQ routing.
- **Idempotency (TRK-01):** one EF transaction — check `ProcessedEvents` by EventId (hit → skip, treat as success); upsert `ShipmentTracking` by TrackingNumber; insert `TrackingEvent`; insert `ProcessedEvent`. A concurrent duplicate hits the `ProcessedEvent.EventId` PK → `DbUpdateException` → transaction rolls back → treated as an already-processed duplicate (success, ack), never a retry.
- **Post-apply side effects (after the commit, failures logged, never rethrown):** delete the cache key for the tracking number; broadcast `ShipmentUpdated` (below).

### Cache-aside (TRK-02)
- `ITrackingCache { GetAsync(trackingNumber), SetAsync(trackingNumber, dto, ttl), DeleteAsync(trackingNumber) }` seam in Tracking.Api; `RedisTrackingCache` implementation on StackExchange.Redis, key `tracking:{trackingNumber-lower}`, value = JSON of `TrackingDto`, **TTL 10 minutes**.
- `GET /api/tracking/{trackingNumber}`: cache hit → 200; miss → EF `ShipmentTracking` (404 if absent) → `SetAsync` → 200. `TrackingDto` camelCase: trackingNumber, description, destination, status, createdAt, updatedAt.
- `GET /api/tracking/{trackingNumber}/events` (timeline, Phase 5 feed) from `TrackingEvent` rows — no cache.
- The consumer deletes the key on every applied event (create or status change) → next read repopulates.

### SignalR (TRK-03)
- `TrackingHub : Hub` with client methods `JoinTrackingGroup(trackingNumber)` / `LeaveTrackingGroup(trackingNumber)` (server-side `Clients.Groups` add/remove; group name = tracking number).
- After each applied event the consumer sends method `ShipmentUpdated` with `TrackingUpdatedDto` (same shape as `TrackingDto`) to `Clients.All` **and** `Clients.Group(trackingNumber)` — via `IHubContext<TrackingHub>` so the handler is unit-testable with a stub.

### DLQ admin (TRK-04, TRK-05)
- `IDlqAdmin { Task<int> CountAsync(); Task<int> ReplayAsync(); }` seam in Tracking.Api.
- `RabbitDlqAdmin` implementation: passive declare of `RabbitTopology.DlqQueue` for the count; replay = `BasicGet` loop from the DLQ (no ack needed — get removes), republish each body verbatim to `RabbitTopology.Exchange` with `RabbitTopology.ShipmentRoutingKey` and **no** `x-retry-count` header (fresh attempt), return the moved count.
- `GET /api/admin/dlq` → `{ "count": n }`; `POST /api/admin/dlq/replay` → `{ "replayed": n }`.

### Broker wiring (`RabbitConsumerBroker` in KargoNet.Messaging + hosted consume loop)
- `RabbitConsumerBroker : IConsumerBroker` — RabbitMQ.Client 7.2.2 connection/channel, `AckAsync` → `BasicAck`, `PublishAsync` → `BasicPublish` with headers; opened lazily (connection on first use, SemaphoreSlim-guarded, same pattern as `LazyOutboxChannelFactory`) so the API starts without a reachable broker.
- `ShipmentEventsConsumer.ExecuteAsync`: connect, passive-declare the topology, `BasicConsume` on `RabbitTopology.MainQueue` with the pipeline handler; connection failures → log + retry loop (2 s), host never dies (Phase 3 WR-02 pattern).

### DI (Tracking.Api Program.cs)
`AddDbContext<TrackingDbContext>` (Npgsql `ConnectionStrings:TrackingDb`), `AddHub<TrackingHub>`, `IHubContext<TrackingHub>`, `IConsumerBroker → RabbitConsumerBroker`, `IDlqAdmin → RabbitDlqAdmin`, `ITrackingCache → RedisTrackingCache` (StackExchange.Redis from `ConnectionStrings:Redis`), `AddHostedService<ShipmentEventsConsumer>`.

## Constraints
- .NET 10; packages pinned explicitly: EF Core/Design/InMemory 10.0.15, Npgsql EF 10.0.3, Mvc.Testing 10.0.15, StackExchange.Redis (latest 2.x — record the resolved version), RabbitMQ.Client 7.2.2 (no new Rabbit packages). xunit 2.9.3 / runner 2.8.2 in tests — never bump.
- No live Postgres/Redis/RabbitMQ in this environment: design-time migration only; live e2e (real broker consume, real Redis, DLQ via compose) is a documented **Phase 6 deferral**.
- Do not modify Phase 2 public surfaces (`ConsumerBase`, `RetryPolicy`, `RabbitTopology`, `IConsumerBroker`, `EventSerializer`) and do not modify Shipment.Api.
- Every consumer failure path ends in ack (Phase 2 invariant); events are never lost.

## Plan sketch
- **04-01**: EF model + `InitialCreate` migration + `ShipmentEventsConsumer` pipeline + `ShipmentEventHandler` (idempotent apply) + InMemory/fake-broker tests.
- **04-02**: `ITrackingCache` + `RedisTrackingCache` + tracking endpoints (cache-aside + timeline) + `IDlqAdmin` + `RabbitDlqAdmin` + DLQ endpoints + tests.
- **04-03**: `TrackingHub` + `ShipmentUpdated` broadcast + `RabbitConsumerBroker` + hosted consume loop + DI + endpoint/hub tests.
