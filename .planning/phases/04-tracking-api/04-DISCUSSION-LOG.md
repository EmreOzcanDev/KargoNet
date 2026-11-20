# Phase 4 Discussion Log — Tracking API

**Date:** 2026-11-20 · **Mode:** autonomous (human gates resolved with safe defaults)

## Decisions

| # | Question | Decision | Rationale |
|---|----------|----------|-----------|
| D1 | Single-type `ConsumerBase<TPayload>` vs two event types | `ShipmentEventsConsumer` implements the pipeline directly over `IConsumerBroker`, mirroring `ConsumerBase`'s contract; deserializes `EventEnvelope<JsonElement?>` and switches on `EventType` | No Phase 2 public surface changes; both event types handled in one consumer; fake-broker testability preserved |
| D2 | Idempotency mechanism | `ProcessedEvent.EventId` PK; check + read-model update + event insert in one EF transaction; concurrent duplicate → `DbUpdateException` → rollback → treat as processed | PLAN.md requirement; PK is the race-safe backstop the check-then-insert pattern needs |
| D3 | Cache seam | `ITrackingCache` in Tracking.Api (not Messaging) with `RedisTrackingCache` (StackExchange.Redis), key `tracking:{number-lower}`, TTL 10 min | Cache-aside is a Tracking.Api concern; seam keeps endpoint/consumer tests broker-free |
| D4 | Cache invalidation strategy | Delete-on-event (consumer deletes key after commit) | PLAN.md: "cache deleted/overwritten on event consumption"; delete avoids stale-write races |
| D5 | SignalR group model | Hub `JoinTrackingGroup`/`LeaveTrackingGroup`; group name = tracking number; broadcast `ShipmentUpdated` to `Clients.All` + group | PLAN.md: all operators + per-tracking-number group (portal may subscribe later) |
| D6 | DLQ admin seam | `IDlqAdmin` (Count/Replay) with `RabbitDlqAdmin`; replay republishes verbatim to `Exchange`/`ShipmentRoutingKey` with no `x-retry-count` | Testable without a broker; replay must start a fresh 3-attempt cycle |
| D7 | Broker-free startup | `RabbitConsumerBroker` lazy-connect (SemaphoreSlim) + consume loop with 2 s retry on connection failure | Same pattern as Phase 3 `LazyOutboxChannelFactory`; host must survive broker/DB downtime |
| D8 | Timeline endpoint | `GET /api/tracking/{trackingNumber}/events` from `TrackingEvent` (no cache) | Phase 5 timeline view needs it; data already stored by the consumer |
| D9 | Live infrastructure | No live broker/Redis in this environment — design-time migration, fake-seam tests; live e2e deferred to Phase 6 | Consistent with Phases 2/3 deferrals; Docker Compose is Phase 6 scope |

## Gates
- **T-04-SC (test strategy):** resolved — InMemory EF + fake `IConsumerBroker`/`ITrackingCache`/`IDlqAdmin` + `IHubContext` stub + `WebApplicationFactory` endpoint tests, matching Phases 2/3.
- **Package versions:** StackExchange.Redis resolved at execution (pin recorded in plan/summary); all other pins fixed as in Phase 3.
- **Docker/live services:** deferred to Phase 6 (blocker already tracked in STATE.md).
