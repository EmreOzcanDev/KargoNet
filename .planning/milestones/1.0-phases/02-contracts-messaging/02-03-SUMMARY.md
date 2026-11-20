---
phase: 02-contracts-messaging
plan: 03
subsystem: messaging
tags: [rabbitmq, publisher-confirms, seam, xunit]

# Dependency graph
requires:
  - "02-02 (RabbitTopology constants, RabbitMQ.Client reference)"
provides:
  - "MessagePublisher.PublishAsync(routingKey, body, headers) with confirm await + PublishNotConfirmedException on unconfirmed"
  - "IOutboxChannel / PublishOutcome seam + real RabbitOutboxChannel adapter (7.x ack API)"
affects: [03, 04, 06]

actuals:
  tasks: 2
  commits: 2
  plan_head_before: b944130
  plan_head_after: 1ba65d5

tech-stack:
  added: [RabbitMQ.Client 7.x confirm flow (GetNextPublishSequenceNumberAsync / BasicPublishAsync / BasicAcksAsync)]
  patterns:
    - "Thin IOutboxChannel seam; publish path unit-testable via a fake channel; live broker-ack deferred to Phase 6"

key-files:
  created:
    - src/BuildingBlocks/KargoNet.Messaging/Publishing/IOutboxChannel.cs
    - src/BuildingBlocks/KargoNet.Messaging/Publishing/MessagePublisher.cs
    - src/BuildingBlocks/KargoNet.Messaging/Publishing/PublishOutcome.cs
    - src/BuildingBlocks/KargoNet.Messaging/Publishing/RabbitOutboxChannel.cs
    - tests/Tracking.Tests/Messaging/MessagePublisherTests.cs
  modified:
    - tests/Tracking.Tests/Messaging/TopologyTests.cs (xUnit2000 assert-arg order)
    - src/BuildingBlocks/KargoNet.Messaging/Publishing/RabbitOutboxChannel.cs (CS8601/8619/8620)
    - src/BuildingBlocks/KargoNet.Messaging/Topology/RabbitTopology.cs (nullable header dictionaries)

key-decisions:
  - "RabbitMQ.Client 7.2.2 has no 8.x confirm API (PublishAsync/PublishResult/WaitForConfirmsAsync); RabbitOutboxChannel uses the 7.x equivalent: GetNextPublishSequenceNumberAsync → BasicPublishAsync → await ack via BasicAcksAsync (multi-ack aware), bounded by Channel.ContinuationTimeout"

patterns-established:
  - "Publishers depend on IOutboxChannel, never on IChannel directly — the seam that makes phases 3–4 unit-testable broker-free"

requirements-completed: [MSG-04]

coverage:
  - id: D1
    description: "SC2 (unit-testable half) — publisher awaits the confirm outcome and throws PublishNotConfirmedException on unconfirmed publish"
    verification:
      - kind: unit
        ref: "MessagePublisherTests — 3/3 pass (confirmed, unconfirmed throws, headers passed through)"
        status: pass
    human_judgment: false
  - id: D2
    description: "Live broker-ack deferral to Phase 6 is explicit and matches CONTEXT"
    verification:
      - kind: other
        ref: "RabbitOutboxChannel implements the real IChannel adapter; plan + CONTEXT document the deferral"
        status: pass
    human_judgment: false

duration: 35min
completed: 2026-11-19
status: complete
---

# Phase 02 Plan 03: Publisher with Confirms Summary

**MessagePublisher awaiting a publish confirm (throwing PublishNotConfirmedException on unconfirmed) over a thin IOutboxChannel seam, with RabbitOutboxChannel as the real 7.x adapter — 3 unit tests; live broker-ack deferred to Phase 6**

## Performance

- **Duration:** ~35 min
- **Tasks:** 2
- **Files modified:** 5 created, 3 modified

## Accomplishments

- `MessagePublisher.PublishAsync(routingKey, body, headers)` to fixed exchange `kargonet.shipments`, awaiting the `PublishOutcome`
- `PublishNotConfirmedException` thrown on unconfirmed publish
- `IOutboxChannel`/`PublishOutcome` seam + real `RabbitOutboxChannel` adapter using the 7.x ack API
- 3 unit tests against a fake channel

## Task Commits

1. **Task 1: MessagePublisher + seam** - `d4ff9ff`
2. **Task 2: RabbitOutboxChannel + tests** - `d4ff9ff`; post-commit warning fixes `1ba65d5` (nullable header dictionaries)

## Files Created/Modified

- `src/BuildingBlocks/KargoNet.Messaging/Publishing/MessagePublisher.cs`
- `src/BuildingBlocks/KargoNet.Messaging/Publishing/IOutboxChannel.cs`
- `src/BuildingBlocks/KargoNet.Messaging/Publishing/PublishOutcome.cs`
- `src/BuildingBlocks/KargoNet.Messaging/Publishing/RabbitOutboxChannel.cs`
- `tests/Tracking.Tests/Messaging/MessagePublisherTests.cs`
- `tests/Tracking.Tests/Messaging/TopologyTests.cs` — xUnit2000 assert-arg order (0-warning bar)
- `src/BuildingBlocks/KargoNet.Messaging/Topology/RabbitTopology.cs` — `IDictionary<string, object?>` matching 7.2.2 client types

## Decisions Made

- 7.2.2 confirm flow: `GetNextPublishSequenceNumberAsync` → `BasicPublishAsync` → await broker ack via `BasicAcksAsync` (multi-ack aware), bounded by `Channel.ContinuationTimeout` — the 7.x equivalent of the plans' 8.x-style confirm contract; `PublishOutcome(IsConfirmed, PublishId)` preserved

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - API availability] 8.x confirm API absent in 7.2.2**
- **Found during:** Task 2 (RabbitOutboxChannel)
- **Issue:** `PublishAsync`/`PublishResult`/`WaitForConfirmsAsync`/`NextPublishSeqNo` do not exist in 7.2.2
- **Fix:** 7.x equivalent (seq number, BasicPublishAsync, BasicAcksAsync) with the same `PublishOutcome` contract
- **Verification:** MessagePublisherTests 3/3; build 0/0
- **Committed in:** d4ff9ff

**2. [Rule 3 - Warnings] CS8601/CS8619/CS8620 on header dictionaries**
- **Found during:** post-commit 0-warning check
- **Issue:** nullable annotation mismatches against the 7.2.2 client types
- **Fix:** `IDictionary<string, object?>` in RabbitOutboxChannel + RabbitTopology
- **Verification:** build 0 warnings / 0 errors
- **Committed in:** 1ba65d5

---

**Total deviations:** 2 auto-fixed (1 API availability, 1 warning cleanup)

## Verification Evidence

```
$ ~/.dotnet/dotnet test KargoNet.sln --filter MessagePublisherTests
Passed!  - Failed: 0, Passed: 3, Skipped: 0
```

---
*Phase: 02-contracts-messaging*
*Completed: 2026-11-19*

## Self-Check: PASSED
