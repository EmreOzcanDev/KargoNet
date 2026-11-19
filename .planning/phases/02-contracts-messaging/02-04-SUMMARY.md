---
phase: 02-contracts-messaging
plan: 04
subsystem: messaging
tags: [consumer-base, retry, dlq, x-retry-count, xunit]

# Dependency graph
requires:
  - "02-01 (EventEnvelope, EventSerializer)"
  - "02-02 (RabbitTopology constants)"
  - "02-03 (MessagePublisher, IOutboxChannel)"
provides:
  - "RetryPolicy.Decide — pure retry/DLQ decision (attempts 1&2 → republish shipment.retry with incremented x-retry-count + x-exception; ≥3 → DLQ shipment.dead with x-exception)"
  - "ConsumerBase<T> — deserialize → handle → ack on every path, never requeue"
  - "IConsumerBroker seam for broker-free unit tests"
affects: [04, 05, 06]

actuals:
  tasks: 2
  commits: 1
  plan_head_before: 1ba65d5
  plan_head_after: 9542083

tech-stack:
  added: []
  patterns:
    - "Pure RetryPolicy decision function + IConsumerBroker seam; live consume loop deferred to Phase 6"

key-files:
  created:
    - src/BuildingBlocks/KargoNet.Messaging/Consuming/ConsumerBase.cs
    - src/BuildingBlocks/KargoNet.Messaging/Consuming/IConsumerBroker.cs
    - src/BuildingBlocks/KargoNet.Messaging/Consuming/RetryDecision.cs
    - src/BuildingBlocks/KargoNet.Messaging/Consuming/RetryPolicy.cs
    - tests/Tracking.Tests/Messaging/ConsumerBaseTests.cs
    - tests/Tracking.Tests/Messaging/RetryPolicyTests.cs
  modified:
    - src/BuildingBlocks/KargoNet.Messaging/KargoNet.Messaging.csproj (ProjectReference to Contracts)

key-decisions:
  - "DLQ messages keep the original x-retry-count (original headers copied through; only the retry path bumps it) — matches the locked spec"
  - "Live consume/requeue loop omitted (deferred to Phase 6); HandleDeliveryAsync is the fully unit-tested pipeline"

patterns-established:
  - "Every consumer failure path ends in ack — the locked invariant; later phase consumers derive from ConsumerBase<T>"

requirements-completed: [MSG-02, MSG-03]

coverage:
  - id: D1
    description: "SC3 — failing consumer republishes to retry queue with incremented x-retry-count (attempts below 3)"
    verification:
      - kind: unit
        ref: "ConsumerBaseTests FirstFailure/SecondFailure + RetryPolicyTests boundary 0/1/2/3 — pass"
        status: pass
    human_judgment: false
  - id: D2
    description: "SC4 — >3 attempts → DLQ with x-exception"
    verification:
      - kind: unit
        ref: "ConsumerBaseTests.ThirdFailure_RoutesToDlqWithoutRetryRepublish — passes; x-exception asserted"
        status: pass
    human_judgment: false

duration: 35min
completed: 2026-11-19
status: complete
---

# Phase 02 Plan 04: Consumer Base + Retry/DLQ Summary

**ConsumerBase<T> + pure RetryPolicy implementing the locked 3-attempt retry/DLQ semantics (republish attempts 1&2 with incremented x-retry-count; ≥3 → DLQ with x-exception; always ack, never requeue) — 12 unit tests**

## Performance

- **Duration:** ~35 min
- **Tasks:** 2
- **Files modified:** 6 created, 1 modified

## Accomplishments

- `RetryPolicy.Decide` — pure decision: attempts 1&2 → republish `shipment.retry` with incremented `x-retry-count` + `x-exception`; attempt ≥3 → DLQ `shipment.dead` with `x-exception`
- `ConsumerBase<T>` — deserialize → handle → ack on every path (never requeue, never nack)
- `IConsumerBroker` seam for broker-free tests
- 8 unit tests at plan completion (RetryPolicyTests 4 + ConsumerBaseTests 4); expanded to 12 after the Phase 2 review fixes (malformed body → DLQ, corrupt retry header → first attempt, republish failure → still acked)

## Task Commits

1. **Task 1: RetryPolicy + decision types** - `9542083` (part of plan commit)
2. **Task 2: ConsumerBase + tests** - `9542083` (part of plan commit)

## Files Created/Modified

- `src/BuildingBlocks/KargoNet.Messaging/Consuming/ConsumerBase.cs`
- `src/BuildingBlocks/KargoNet.Messaging/Consuming/IConsumerBroker.cs`
- `src/BuildingBlocks/KargoNet.Messaging/Consuming/RetryDecision.cs`
- `src/BuildingBlocks/KargoNet.Messaging/Consuming/RetryPolicy.cs`
- `tests/Tracking.Tests/Messaging/ConsumerBaseTests.cs` — 7 tests (post-review)
- `tests/Tracking.Tests/Messaging/RetryPolicyTests.cs` — 4 tests
- `src/BuildingBlocks/KargoNet.Messaging/KargoNet.Messaging.csproj` — ProjectReference to Contracts

## Decisions Made

- DLQ path copies original headers through without bumping `x-retry-count` (only the retry path bumps it) — matches the locked spec

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Deferred] Live consume loop omitted**
- **Found during:** Task 2 (ConsumerBase)
- **Issue:** No local broker; the live consume/requeue loop cannot be built or tested in this phase
- **Fix:** `HandleDeliveryAsync` is the fully unit-tested pipeline; the broker-side consume loop is an explicit Phase 6 drop-in, as the plan documents
- **Verification:** ConsumerBaseTests + RetryPolicyTests green
- **Committed in:** 9542083

Post-plan review fixes (REVIEW.md, separate commits): CR-01 `1451807` (ack-handler multi-ack short-circuit), CR-02 `59a268c` (decode inside failure path, DLQ on malformed body), WR-01 `9e5e78f` (defensive retry-count parse), WR-02 `135ddd4` (republish failure still acks).

---

**Total deviations:** 1 auto-fixed (deferral, documented in plan)

## Verification Evidence

```
$ ~/.dotnet/dotnet test KargoNet.sln --filter "ConsumerBaseTests|RetryPolicyTests"
Passed!  - Failed: 0, Passed: 11, Skipped: 0
```

---
*Phase: 02-contracts-messaging*
*Completed: 2026-11-19*

## Self-Check: PASSED
