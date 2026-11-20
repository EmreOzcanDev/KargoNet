---
gsd_state_version: "1.0"
milestone: v1.0
milestone_name: KargoNet
current_plan: Not started
status: Not started
stopped_at: Phase 4 complete, ready to plan Phase 5
last_updated: "2026-11-20T06:38:47.501Z"
state_head: e46b4c692e834d1e2b4c14cd8ec13b7b3a5ce6a6
progress:
  total_phases: 6
  completed_phases: 4
  total_plans: 13
  completed_plans: 13
  percent: 67
last_activity: 2026-11-19
current_phase: 5
current_phase_name: Web Frontends
last_activity_desc: STATE.md frontmatter restored
---

# Project State

## Project Reference

- **Project**: KargoNet
- **Context**: See [PROJECT.md](./PROJECT.md) for goals, requirements, and constraints.
- **Roadmap**: See [ROADMAP.md](./ROADMAP.md) for phase structure and success criteria.

**Core value:** Reliable shipment status propagation: a status change made in the operator UI must reach the customer portal and the operator's live view without loss, even when RabbitMQ or individual consumers are temporarily down.
**Current focus:** Phase 5 — Web Frontends

## Current Position
Current Plan: Not started
Total Plans in Phase: TBD


- **Phase**: 5 (Web Frontends)
- **Status**: Ready to plan
- **Last Activity**: 2026-11-20 — Phase 4 complete (verification 4/4 passed, 78/78 tests), transitioned to Phase 5

## Performance Metrics

| Metric | Value |
|--------|-------|
| Phases completed | 4/6 |
| Requirements validated | 13/25 |
**Per-Plan Metrics:**

| Plan | Duration | Tasks | Files |
|------|----------|-------|-------|
| Phase 1 P01 | 13m | 2 tasks | 7 files |
| Phase 1 P02 | 12m | 2 tasks | 23 files |
| Phase 1 P03 | 11m | 2 tasks | 12 files |
| Phase 2 P01 | 25m | 2 tasks | 5 files |
| Phase 2 P02 | 25m | 2 tasks | 5 files |
| Phase 2 P03 | 35m | 2 tasks | 8 files |
| Phase 2 P04 | 35m | 2 tasks | 7 files |
| Phase 3 P01 | 40m | 2 tasks | 10 files |
| Phase 3 P02 | 45m | 2 tasks | 6 files |
| Phase 3 P03 | 30m | 2 tasks | 6 files |
| Phase 4 P01 | 95m | 3 tasks | 15 files |
| Phase 4 P02 | 150m | 3 tasks | 13 files |
| Phase 4 P03 | 200m | 3 tasks | 10 files |

## Accumulated Context

### Decisions
- [Phase 1]: Phase 1 uses classic .sln (dotnet new sln --format sln); .NET 10 defaults to .slnx but plan/ROADMAP pin KargoNet.sln
- [Phase 1]: GET /health serializes ShipmentStatus via Enum.GetNames to expose the seven lifecycle names, proving the Contracts ProjectReference is live
- [Phase 1]: @angular/cli verified legitimate (official Angular org) — T-01-SC human gate cleared 2026-11-19; operator-ui scaffolded --ssr false as client-side SPA
- [Phase 1]: KargoNet.Messaging left empty (consumer base/retry/DLQ are Phase 2 scope); reference graph Tracking.Api->Contracts+Messaging, Portal.Web->Contracts
- [Phase 2]: EventEnvelope { EventId, EventType, OccurredAt, JsonElement? Payload } with camelCase System.Text.Json EventSerializer; payload records ShipmentCreatedPayload / ShipmentStatusChangedPayload
- [Phase 2]: Topic exchange kargonet.shipments; shipments.main<-shipment, shipments.retry<-shipment.retry (x-message-ttl=5000, dead-letter back to shipment), shipments.dlq<-shipment.dead
- [Phase 2]: 3-attempt semantics — attempt 1 = first delivery; republish attempts 1&2 with incremented x-retry-count + x-exception; >=3 -> DLQ with x-exception; always ack, never requeue
- [Phase 2]: RabbitMQ.Client pinned 7.2.2 (AGENTS.md pins 7.x; 7.x confirm API: seq no + BasicPublishAsync + BasicAcksAsync)
- [Phase 2]: IOutboxChannel seam + pure RetryPolicy.Decide make publish/retry unit-testable broker-free; live broker behavior deferred to Phase 6
- [Phase 3]: Shipment + OutboxMessage entities, string-converted statuses, unique TrackingNumber, (Status,CreatedAt) outbox index; InitialCreate migration design-time (dotnet-ef 10.0.15)
- [Phase 3]: Event types shipment.created / shipment.status.changed; both write paths (POST create, PUT status) persist entity + outbox row in one EF transaction
- [Phase 3]: OutboxPublisherService: 2 s poll, batch 50, verbatim PayloadJson to routing key shipment, per-row confirm-mark in own transaction, failure leaves Pending (never delete), survives poll DB failures
- [Phase 3]: LazyOutboxChannelFactory (SemaphoreSlim-guarded) opens the broker channel on first publish — API starts without a reachable broker
- [Phase 3]: EF Core/Design/InMemory 10.0.15 + Npgsql EF 10.0.3 pinned; tests use Database:Provider config switch (DI-swap routes dead on EF Core 10)
- [Phase 4]: ShipmentEventsConsumer implements the IConsumerBroker delivery pipeline directly (NOT a ConsumerBase<T> subclass — that seam is single-type), mirroring ConsumerBase's contract exactly (x-retry-count/x-exception headers, RetryPolicy.Decide order, always ack)
- [Phase 4]: TRK-01 idempotency: one EF transaction — ProcessedEvents marker check -> ShipmentTracking upsert -> TrackingEvent append -> marker insert; DbUpdateException re-checks the marker post-rollback (present = D2 race, success; absent = rethrow -> retry -> DLQ); stale-event guard never regresses an existing row (review WR-06)
- [Phase 4]: Redis cache-aside: key tracking:{number-lower}, 10-min TTL, delete post-commit; GET degrades to EF on cache failure (WR-04); timeline endpoint uncached; StackExchange.Redis pinned 2.13.23 (latest stable 2.x)
- [Phase 4]: SignalR TrackingHub at /hubs/tracking with Join/Leave tracking-number groups; ShipmentUpdated broadcast to Clients.All + group via scoped IShippingUpdateBroadcaster (IHubContext seam)
- [Phase 4]: RabbitConsumerBroker implements the frozen IConsumerBroker and gains ConsumeAsync as a class member; LazyChannelFactory uses per-operation channels (channel not thread-safe); BasicQos prefetch 10; hosted consume loop retries on 2 s, host never dies
- [Phase 4]: DLQ admin: passive-declare count + drain-and-republish verbatim to shipment routing key with NO x-retry-count, cancellable, per-op channel

### Todos
- (none yet)

### Blockers

- ⚠️ [Phase 6] Docker/Docker Compose not installed; daemon install requires root — user granted full install authority, attempt install in Phase 6

## Session Continuity

Last session: 2026-11-20T23:30:00Z
Stopped at: Phase 4 complete, ready to plan Phase 5
Resume file: None

*Last updated: 2026-11-20 after Phase 4*
