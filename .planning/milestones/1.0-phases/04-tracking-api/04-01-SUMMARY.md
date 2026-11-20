---
phase: 04-tracking-api
plan: 01
subsystem: tracking-read-model
tags: [ef-core, inmemory, migration, idempotency, consumer, xunit]

# Dependency graph
requires: [02, 03]
provides:
  - "ShipmentTracking / TrackingEvent / ProcessedEvent entities + TrackingDbContext (Npgsql) with PKs (TrackingNumber, EventId, EventId) and a (TrackingNumber, OccurredAt) index on TrackingEvent; InitialCreate migration generated design-time (no live DB)"
  - "ShipmentEventHandler — one-transaction idempotent apply (ProcessedEvents check → upsert → TrackingEvent → ProcessedEvent), DbUpdateException race treated as success, post-commit side effects (cache delete + broadcast) logged and never rethrown"
  - "ShipmentEventsConsumer — ConsumerBase-mirroring delivery over IConsumerBroker (shipment.created / shipment.status.changed only; same x-retry-count / x-retry headers, RetryPolicy.Decide order, always-ack)"
  - "ITrackingCache / IShippingUpdateBroadcaster / TrackingUpdatedDto seams (impls land in plans 04-02 / 04-03)"
affects: [04-02, 04-03, 06]

actuals:
  tasks: 3
  commits: 1
  plan_head_before: 0bed3ec
  plan_head_after: 117876c

tech-stack:
  added:
    - "Microsoft.EntityFrameworkCore 10.0.15 (Tracking.Api)"
    - "Microsoft.EntityFrameworkCore.Design 10.0.15 (Tracking.Api, Private)"
    - "Microsoft.EntityFrameworkCore.InMemory 10.0.15 (Tracking.Api, Private)"
    - "Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 (Tracking.Api)"
  patterns:
    - "Idempotency inside ONE BeginTransactionAsync: ProcessedEvents.Any check first, then upsert + timeline + marker; DbUpdateException (concurrent PK insert) caught, rolled back, returned as success"
    - "Post-commit side effects each in their own try/catch — log-and-swallow, never rethrow into the ack path"
    - "Consumer mirrors ConsumerBase verbatim: decode inside the failure path, ParseRetryCount defensive int/long parse, RetryPolicy.Decide, best-effort republish (swallowed), unconditional AckAsync"

key-files:
  created:
    - src/Services/Tracking.Api/Entities/ShipmentTracking.cs
    - src/Services/Tracking.Api/Entities/TrackingEvent.cs
    - src/Services/Tracking.Api/Entities/ProcessedEvent.cs
    - src/Services/Tracking.Api/Data/TrackingDbContext.cs
    - src/Services/Tracking.Api/Data/TrackingDbContextFactory.cs
    - src/Services/Tracking.Api/Migrations/20261120020217_InitialCreate.cs
    - src/Services/Tracking.Api/Migrations/20261120020217_InitialCreate.Designer.cs
    - src/Services/Tracking.Api/Migrations/TrackingDbContextModelSnapshot.cs
    - src/Services/Tracking.Api/Consuming/ShipmentEventHandler.cs
    - src/Services/Tracking.Api/Consuming/ShipmentEventsConsumer.cs
    - src/Services/Tracking.Api/Tracking/ITrackingCache.cs
    - src/Services/Tracking.Api/Tracking/IShipmentUpdateBroadcaster.cs
    - src/Services/Tracking.Api/Tracking/TrackingUpdatedDto.cs
    - src/Services/Tracking.Api/Program.cs
    - tests/KargoNet.Tracking.Tests/Data/TrackingDbContextTests.cs
    - tests/KargoNet.Tracking.Tests/Consuming/ShipmentEventsConsumerTests.cs
  modified:
    - src/Services/Tracking.Api/Tracking.Api.csproj
    - tests/KargoNet.Tracking.Tests/KargoNet.Tracking.Tests.csproj

key-decisions:
  - "`internal partial class Program` in Tracking.Api (plus a temporary throwing `Main`) so the Web-SDK synthesized `Program` is internal — a public one collides (CS0433) with Shipment.Api's public `Program` in the shared test assembly; the full host lands in 04-02 and the marker type for `WebApplicationFactory<T>` arrives with it"
  - "`ITrackingCache`/`IShipmentUpdateBroadcaster` value types are `TrackingUpdatedDto` (the plan's 'minimal surface' payload) — the interface file is frozen for 04-02, whose Redis impl implements it as declared"
  - "Payload decoding uses the same wire options as `EventSerializer` (camelCase + `JsonStringEnumConverter`) — the consumer's own `PayloadOptions` replicate them because `EventSerializer` (Phase 2 surface) is not modifiable"
  - "Test DbContexts use `.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` — the handler's real transaction is the feature under test; the InMemory provider simply ignores it"

patterns-established:
  - "Exactly-once apply is enforced by the ProcessedEvent PK inside the same transaction as the read-model writes; redelivery short-circuits to success before any side effect"
  - "The consumer's only public surface is `HandleDeliveryAsync(deliveryTag, body, headers)` — the 04-03 BackgroundService wraps it without touching the pipeline"

requirements-completed: [TRK-01]

coverage:
  - id: T1
    description: "Entities + TrackingDbContext with the three PKs and the (TrackingNumber, OccurredAt) index; InitialCreate migration exists, generated design-time"
    verification:
      - kind: unit
        ref: "TrackingDbContextTests.Model_shape_carries_three_pks_and_the_tracking_event_composite_index, TrackingDbContextTests.Round_trip_smoke_inserts_and_queries_all_three_read_model_rows — pass"
      - kind: artifact
        ref: "Migrations/20261120020217_InitialCreate.cs (+ Designer + ModelSnapshot)"
    human_judgment: false
  - id: T2
    description: "One-transaction idempotent apply: ProcessedEvents hit → skip as success; redelivered EventId applied exactly once"
    verification:
      - kind: unit
        ref: "ShipmentEventsConsumerTests.Redelivered_same_event_id_is_applied_exactly_once_and_ackd — pass"
    human_judgment: false
  - id: T3
    description: "Concurrent duplicate hitting the ProcessedEvent PK raises DbUpdateException → treated as already-processed success, never a retry"
    verification:
      - kind: unit
        ref: "ShipmentEventsConsumerTests.Race_backstop_duplicate_processed_marker_returns_success_without_rethrowing — pass (InMemory cannot force a true concurrent insert collision; the test exercises the deterministic skip path the handler takes in the same situation, per the plan's note)"
    human_judgment: false
  - id: T4
    description: "Consumer deserializes EventEnvelope<JsonElement?>, dispatches on exactly two event types, mirrors ConsumerBase (headers, RetryPolicy.Decide order, always ack) over IConsumerBroker"
    verification:
      - kind: unit
        ref: "ShipmentEventsConsumerTests.Unknown_event_type_routes_to_retry_then_dlq_and_always_ackd, ShipmentEventsConsumerTests.Handler_failure_routes_to_retry_then_dlq_and_always_ackd — pass"
    human_judgment: false
  - id: T5
    description: "Post-apply side effects (cache delete, broadcast) run after commit, are logged on failure, and are never rethrown"
    verification:
      - kind: unit
        ref: "ShipmentEventsConsumerTests.Created_body_applies_read_model_timeline_and_marker_and_side_effects, ShipmentEventsConsumerTests.Status_changed_body_updates_status_and_appends_timeline_row — pass (side-effect counts asserted; failure paths logged-and-swallowed in source)"
    human_judgment: false

duration: 95min
completed: 2026-11-20
status: complete
---

# Phase 04 Plan 01: Tracking Read Model + Idempotent Event Pipeline Summary

**EF Core read model (ShipmentTracking / TrackingEvent / ProcessedEvent) with a design-time InitialCreate migration, a one-transaction idempotent apply handler, and a ConsumerBase-mirroring delivery pipeline — proven by 8 unit tests over InMemory + fakes, no live services**

## Performance

- **Duration:** ~95 min
- **Tasks:** 3
- **Files modified:** 16 created, 2 modified

## Accomplishments

- `ShipmentTracking` (TrackingNumber PK, Status default `Created`), `TrackingEvent` (EventId PK + `(TrackingNumber, OccurredAt)` index), `ProcessedEvent` (EventId PK) entities; `TrackingDbContext` + `IDesignTimeDbContextFactory`; `InitialCreate` migration generated with `dotnet ef` against the design-time factory (no live database)
- `ShipmentEventHandler.ApplyCreatedAsync` / `ApplyStatusChangedAsync` — one `BeginTransactionAsync`: `ProcessedEvents.Any(EventId)` → skip as success; upsert `ShipmentTracking` by tracking number; insert `TrackingEvent`; insert `ProcessedEvent`; commit. `DbUpdateException` (concurrent duplicate) → rollback → success. Post-commit: `ITrackingCache.DeleteAsync` then `IShipmentUpdateBroadcaster.BroadcastAsync`, each in its own try/catch (log-and-swallow)
- `ShipmentEventsConsumer` — `HandleDeliveryAsync` decodes `EventEnvelope<JsonElement?>` inside the failure path, dispatches on exactly `shipment.created` / `shipment.status.changed`, mirrors `ConsumerBase` verbatim (defensive `x-retry-count` parse, `x-exception` header, `RetryPolicy.Decide` order, best-effort republish to retry/DLQ routing keys, unconditional ack). Payload decoding uses wire-format options (camelCase + `JsonStringEnumConverter`) matching `EventSerializer`
- 8 unit tests: model shape (PKs + composite index), round-trip, created apply (row + timeline + marker + side effects + ack), exactly-once redelivery, status change, unknown type → retry → DLQ, forced handler failure → retry → DLQ, race backstop

## Task Commits

1. **Task 1: Entities, context, migration** - `117876c` (plan commit)
2. **Task 2: Handler + consumer + seams** - `117876c` (plan commit)
3. **Task 3: Tests (TDD)** - `117876c` (plan commit)

## Files Created/Modified

- `src/Services/Tracking.Api/Entities/{ShipmentTracking,TrackingEvent,ProcessedEvent}.cs` — read-model entities
- `src/Services/Tracking.Api/Data/{TrackingDbContext,TrackingDbContextFactory}.cs` — context + design-time factory
- `src/Services/Tracking.Api/Migrations/20261120020217_InitialCreate.cs` (+ Designer + ModelSnapshot)
- `src/Services/Tracking.Api/Consuming/{ShipmentEventHandler,ShipmentEventsConsumer}.cs`
- `src/Services/Tracking.Api/Tracking/{ITrackingCache,IShipmentUpdateBroadcaster,TrackingUpdatedDto}.cs` — seams for 04-02/04-03
- `src/Services/Tracking.Api/Program.cs` — `internal partial class Program` + temporary throwing `Main` (real host in 04-02)
- `tests/KargoNet.Tracking.Tests/Data/TrackingDbContextTests.cs` — 2 tests
- `tests/KargoNet.Tracking.Tests/Consuming/ShipmentEventsConsumerTests.cs` — 6 tests (RecordingBroker/Cache/Broadcaster fakes, InMemory context with `TransactionIgnoredWarning` suppressed)
- `src/Services/Tracking.Api/Tracking.Api.csproj` — EF Core 10.0.15 / Design / InMemory, Npgsql EF 10.0.3 (all pinned with `-v`)
- `tests/KargoNet.Tracking.Tests/KargoNet.Tracking.Tests.csproj` — plain ProjectReference to Tracking.Api

## Decisions Made

- `ITrackingCache`/`IShipmentUpdateBroadcaster` carry `TrackingUpdatedDto` (the plan's minimal payload) — `ITrackingCache.cs` is not in 04-02's files list, so the interface is frozen as written and 04-02's `RedisTrackingCache` implements it as declared
- `Program.cs` is `internal partial` with a temporary `Main`: the Web-SDK synthesized `Program` would be public and collide (CS0433) with Shipment.Api's public `Program` in the shared test assembly; the full host + `WebApplicationFactory` marker type land in 04-02
- Payload decoding options replicate `EventSerializer`'s (camelCase + enum names) inside the consumer because the Phase 2 `EventSerializer` surface is not modifiable

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking issue] `Program.cs` created ahead of plan 04-02 (not in this plan's files list)**
- **Found during:** Task 1 (first solution build)
- **Issue:** The test project references both Shipment.Api and Tracking.Api; each Web-SDK project synthesizes a public `Program`, producing CS0433 in `tests/KargoNet.Tracking.Tests/Api/ShipmentEndpointTests.cs`. `GlobalNamespaceAlias`, `<Reference><ExternAlias>`, and `ExcludeAssets="compile"` were all verified dead ends under SDK 10.0.404
- **Fix:** `internal partial class Program` in Tracking.Api (synthesized `Program` becomes internal → invisible from the test assembly) plus a temporary throwing `static void Main()`; 04-02 replaces the temporary `Main` with the real host and keeps the class internal, adding a public marker type for `WebApplicationFactory<T>`
- **Files modified:** src/Services/Tracking.Api/Program.cs
- **Verification:** build 0/0
- **Committed in:** 117876c

**2. [Rule 1 - Bug] Consumer payload decoding used default STJ options**
- **Found during:** Task 3 (created/status tests failed — no read-model rows applied; all deliveries routed to retry)
- **Issue:** `JsonElement.Deserialize<T>()` with default options is PascalCase and has no enum converter, while payloads are published through `EventSerializer` (camelCase + `JsonStringEnumConverter`); required positional record parameters made every decode throw
- **Fix:** private static `PayloadOptions` in the consumer replicating the wire format; both payload decodes use it
- **Files modified:** src/Services/Tracking.Api/Consuming/ShipmentEventsConsumer.cs
- **Verification:** all 8 Task 3 tests pass
- **Committed in:** 117876c

---

**Total deviations:** 2 auto-fixed (1 build blocker, 1 wire-format bug)

## Verification Evidence

```
$ ~/.dotnet/dotnet build KargoNet.sln
0 Warning(s) 0 Error(s)

$ ~/.dotnet/dotnet test tests/KargoNet.Tracking.Tests/KargoNet.Tracking.Tests.csproj --filter "FullyQualifiedName~TrackingDbContextTests|FullyQualifiedName~ShipmentEventsConsumerTests"
Passed!  - Failed: 0, Passed: 8, Skipped: 0
```

---
*Phase: 04-tracking-api*
*Completed: 2026-11-20*

## Self-Check: PASSED
