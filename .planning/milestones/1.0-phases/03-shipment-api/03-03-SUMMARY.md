---
phase: 03-shipment-api
plan: 03
subsystem: publishing
tags: [background-service, outbox, rabbitmq, publisher-confirms, xunit]

# Dependency graph
requires: [03-02, 02]
provides:
  - "OutboxPublisherService (BackgroundService) — polls Pending rows every 2 s in batches of 50 ordered by CreatedAt, publishes stored PayloadJson verbatim to routing key shipment, marks Published + PublishedAt on confirm, leaves Pending on failure (never deletes)"
  - "LazyOutboxChannelFactory (IOutboxChannel) — broker connection + confirm channel opened on first publish, cached afterwards; host starts without a reachable broker"
  - "Completed Program.cs DI: ShipmentDbContext, IOutboxChannel → LazyOutboxChannelFactory, MessagePublisher, AddHostedService<OutboxPublisherService>"
affects: [06]

actuals:
  tasks: 2
  commits: 1
  plan_head_before: 69636e9
  plan_head_after: 22b6cf3

tech-stack:
  added: [Microsoft.Extensions.Configuration.Abstractions 10.0.15 (KargoNet.Messaging)]
  patterns:
    - "Per-row publish → own short transaction for the Published mark, so one failure cannot block the rest of the batch"
    - "Failure path = ChangeTracker.Clear() + LogWarning + leave Pending; the 2 s poll is the retry"
    - "Directly-drivable PollOnceAsync — unit-tested with InMemory + a fake IOutboxChannel, no broker, no timer"

key-files:
  created:
    - src/Services/Shipment.Api/Publishing/OutboxPublisherService.cs
    - src/BuildingBlocks/KargoNet.Messaging/Publishing/LazyOutboxChannelFactory.cs
    - tests/KargoNet.Tracking.Tests/Publishing/OutboxPublisherServiceTests.cs
  modified:
    - src/Services/Shipment.Api/Program.cs
    - src/Services/Shipment.Api/Shipment.Api.csproj
    - src/BuildingBlocks/KargoNet.Messaging/KargoNet.Messaging.csproj

key-decisions:
  - "LazyOutboxChannelFactory takes IConfiguration (connection string 'RabbitMq', fallback amqp://localhost) and wraps the Phase 2 RabbitOutboxChannel internally — the DI registration target is the factory, not RabbitOutboxChannel directly, which is what makes 'starts without a broker' true"
  - "A failed connection attempt leaves the cached channel null, so the next poll retries the connection instead of rethrowing from a faulted cache"
  - "PollOnceAsync is deliberately free of delay/timer logic — ExecuteAsync owns the 2 s loop"

patterns-established:
  - "Outbox durability: pending rows survive broker downtime and are retried by the poll loop; confirmed rows are the only rows marked Published"

requirements-completed: [SHIP-04]

coverage:
  - id: D1
    description: "Pending row publishes verbatim (exchange kargonet.shipments, key shipment, exact stored bytes) and is marked Published + PublishedAt"
    verification:
      - kind: unit
        ref: "OutboxPublisherServiceTests.PendingRow_PublishesVerbatimAndIsMarkedPublished — pass"
        status: pass
    human_judgment: false
  - id: D2
    description: "Unconfirmed publish → row stays Pending (never deleted) and is retried on the next poll"
    verification:
      - kind: unit
        ref: "OutboxPublisherServiceTests.UnconfirmedPublish_LeavesRowPendingAndRetriesOnNextPoll — pass"
        status: pass
    human_judgment: false
  - id: D3
    description: "Published rows are skipped; batch capped at 50 (oldest first)"
    verification:
      - kind: unit
        ref: "OutboxPublisherServiceTests — PublishedRow_IsSkippedWithoutPublishCall, BatchIsCappedAtFifty_OldestFirst — pass"
        status: pass
    human_judgment: false

duration: 30min
completed: 2026-11-20
status: complete
---

# Phase 03 Plan 03: Outbox Publisher + DI Wiring Summary

**OutboxPublisherService (2 s / batch-50 / per-row-confirm / leave-pending) plus LazyOutboxChannelFactory so the API starts without a reachable broker, with complete Program.cs DI — proven by 4 unit tests over InMemory + a fake IOutboxChannel**

## Performance

- **Duration:** ~30 min
- **Tasks:** 2
- **Files modified:** 3 created, 3 modified

## Accomplishments

- `OutboxPublisherService : BackgroundService` — `PollOnceAsync` polls `WHERE Status = Pending ORDER BY CreatedAt LIMIT 50`; per row publishes stored `PayloadJson` verbatim to `RabbitTopology.ShipmentRoutingKey` via `MessagePublisher`; on confirm marks `Published` + `PublishedAt` in its own short transaction; on any failure logs, clears the change tracker, leaves the row `Pending` (never deletes) and continues; `ExecuteAsync` loops with a 2 s delay until host shutdown
- `LazyOutboxChannelFactory : IOutboxChannel` — opens connection + `CreateChannelOptions(publisherConfirmationsEnabled: true)` channel on FIRST publish, wraps the Phase 2 `RabbitOutboxChannel`, caches afterwards; a failed open leaves the cache null so the next poll retries
- `Program.cs` — registers `IOutboxChannel → LazyOutboxChannelFactory` (singleton), `MessagePublisher` (singleton), `AddHostedService<OutboxPublisherService>`; `/health` and shipment endpoints untouched
- 4 unit tests: verbatim publish + mark, failure leaves pending + retry, skip published, batch cap of 50

## Task Commits

1. **Task 1: Publisher + DI wiring (tracer)** - `22b6cf3` (part of plan commit)
2. **Task 2: Publisher tests (TDD)** - `22b6cf3` (part of plan commit)

## Files Created/Modified

- `src/Services/Shipment.Api/Publishing/OutboxPublisherService.cs` — BackgroundService + drivable `PollOnceAsync`
- `src/BuildingBlocks/KargoNet.Messaging/Publishing/LazyOutboxChannelFactory.cs` — lazy broker channel
- `tests/KargoNet.Tracking.Tests/Publishing/OutboxPublisherServiceTests.cs` — 4 tests (fake IOutboxChannel, real MessagePublisher, InMemory ctx with TransactionIgnoredWarning suppressed)
- `src/Services/Shipment.Api/Program.cs` — three new registrations
- `src/Services/Shipment.Api/Shipment.Api.csproj` — ProjectReference to KargoNet.Messaging
- `src/BuildingBlocks/KargoNet.Messaging/KargoNet.Messaging.csproj` — Configuration.Abstractions 10.0.15

## Decisions Made

- Plan verification text says `IOutboxChannel → RabbitOutboxChannel`; the plan's own files list + action step 3 specify `LazyOutboxChannelFactory`, which IS the registration (it wraps `RabbitOutboxChannel` internally) — followed the action/files
- Live-broker e2e (real publish over compose) remains a Phase 6 deferral; the unit-testable halves are proven here

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking issue] Microsoft.Extensions.Configuration.Abstractions 10.0.15 added to KargoNet.Messaging**
- **Found during:** Task 1 (lazy channel factory)
- **Issue:** `LazyOutboxChannelFactory` needs `IConfiguration` for the connection string, but the Messaging class library had no reference providing it (not in the plan's file list)
- **Fix:** pinned `Microsoft.Extensions.Configuration.Abstractions 10.0.15` (the version the net10 AspNetCore pack already resolves)
- **Files modified:** KargoNet.Messaging.csproj
- **Verification:** build 0/0
- **Committed in:** 22b6cf3

**2. [Rule 3 - Blocking issue] CreateChannelOptions positional constructor**
- **Found during:** Task 1 (lazy channel factory)
- **Issue:** RabbitMQ.Client 7.2.2's `CreateChannelOptions` has no object-initializer form and no `PublisherConfirms`/`dispatchToThreadPool` named parameters
- **Fix:** `new CreateChannelOptions(true, false)` — positional (publisherConfirmationsEnabled, dispatchToThreadPool)
- **Files modified:** LazyOutboxChannelFactory.cs
- **Verification:** build 0/0
- **Committed in:** 22b6cf3

---

**Total deviations:** 2 auto-fixed (1 missing reference, 1 API shape)

## Verification Evidence

```
$ ~/.dotnet/dotnet build KargoNet.sln
0 Warning(s) 0 Error(s)

$ ~/.dotnet/dotnet test tests/KargoNet.Tracking.Tests/KargoNet.Tracking.Tests.csproj --filter "FullyQualifiedName~OutboxPublisherServiceTests"
Passed!  - Failed: 0, Passed: 4, Skipped: 0

$ ~/.dotnet/dotnet test KargoNet.sln
Passed!  - Failed: 0, Passed: 38, Skipped: 0
```

---
*Phase: 03-shipment-api*
*Completed: 2026-11-20*

## Self-Check: PASSED
