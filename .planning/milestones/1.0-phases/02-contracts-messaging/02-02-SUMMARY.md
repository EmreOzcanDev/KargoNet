---
phase: 02-contracts-messaging
plan: 02
subsystem: messaging
tags: [rabbitmq, topology, dlq, ttl, xunit]

# Dependency graph
requires: []
provides:
  - "TopologySpec — data-only description of the full RabbitMQ topology"
  - "RabbitTopology — name constants + DeclareAsync(IChannel) applying the spec"
  - "KargoNet.Messaging referencing RabbitMQ.Client 7.2.2 (pinned)"
affects: [02, 03, 04, 06]

actuals:
  tasks: 2
  commits: 1
  plan_head_before: 67cf770
  plan_head_after: b944130

tech-stack:
  added: [RabbitMQ.Client 7.2.2 (pinned; AGENTS.md pins 7.x)]
  patterns:
    - "Data-only TopologySpec asserted in unit tests; RabbitTopology applies it to an IChannel"

key-files:
  created:
    - src/BuildingBlocks/KargoNet.Messaging/Topology/TopologySpec.cs
    - src/BuildingBlocks/KargoNet.Messaging/Topology/RabbitTopology.cs
    - tests/Tracking.Tests/Messaging/TopologyTests.cs
  modified:
    - src/BuildingBlocks/KargoNet.Messaging/KargoNet.Messaging.csproj (RabbitMQ.Client 7.2.2)
    - tests/Tracking.Tests/KargoNet.Tracking.Tests.csproj (ProjectReference to Messaging)

key-decisions:
  - "RabbitMQ.Client pinned to 7.2.2 explicitly (`dotnet add package ... -v 7.2.2`) — plan-checker warning: unversioned add would pull latest stable (8.x by Nov 2026) and violate the AGENTS.md 7.x pin"

patterns-established:
  - "Topology names/keys/args captured in TopologySpec; RabbitTopology exposes the constants (Exchange, MainQueue, RetryQueue, DlqQueue, MainRoutingKey, RetryRoutingKey, DlqRoutingKey) consumed by plans 02-03/02-04"

requirements-completed: [MSG-01]

coverage:
  - id: D1
    description: "SC1 — topic exchange kargonet.shipments + main/retry(5s TTL)/DLQ with dead-letter routing"
    verification:
      - kind: unit
        ref: "TopologyTests — 5/5 pass: exact exchange/queue names, binding keys, x-message-ttl=5000, x-dead-letter-exchange=kargonet.shipments, x-dead-letter-routing-key=shipment"
        status: pass
    human_judgment: false
  - id: D2
    description: "RabbitMQ.Client resolves to 7.x"
    verification:
      - kind: other
        ref: "csproj pins RabbitMQ.Client 7.2.2; dotnet build 0/0"
        status: pass
    human_judgment: false

duration: 25min
completed: 2026-11-19
status: complete
---

# Phase 02 Plan 02: RabbitMQ Topology Summary

**Data-only TopologySpec + RabbitTopology.DeclareAsync declaring the locked topology (topic exchange, main/retry 5s-TTL/DLQ with dead-letter routing), RabbitMQ.Client pinned 7.2.2, proven by 5 broker-free unit tests**

## Performance

- **Duration:** ~25 min
- **Tasks:** 2
- **Files modified:** 3 created, 2 modified

## Accomplishments

- `TopologySpec` capturing the exact locked topology: exchange `kargonet.shipments` (topic); `shipments.main` ← `shipment`; `shipments.retry` ← `shipment.retry` with `x-message-ttl=5000`, `x-dead-letter-exchange=kargonet.shipments`, `x-dead-letter-routing-key=shipment`; `shipments.dlq` ← `shipment.dead`
- `RabbitTopology` exposing the name/routing-key constants and `DeclareAsync(IChannel)` issuing the 7.x async declare/bind calls
- RabbitMQ.Client pinned to 7.2.2 (7.x line per AGENTS.md)
- 5 unit tests asserting names, keys, and dead-letter arguments verbatim

## Task Commits

1. **Task 1: Add RabbitMQ.Client 7.2.2** - `b944130` (part of plan commit)
2. **Task 2: TopologySpec + RabbitTopology + tests** - `b944130` (part of plan commit)

## Files Created/Modified

- `src/BuildingBlocks/KargoNet.Messaging/Topology/TopologySpec.cs` — data-only topology description
- `src/BuildingBlocks/KargoNet.Messaging/Topology/RabbitTopology.cs` — constants + DeclareAsync
- `tests/Tracking.Tests/Messaging/TopologyTests.cs` — 5 tests
- `src/BuildingBlocks/KargoNet.Messaging/KargoNet.Messaging.csproj` — RabbitMQ.Client 7.2.2
- `tests/Tracking.Tests/KargoNet.Tracking.Tests.csproj` — ProjectReference to Messaging

## Decisions Made

- Explicit `-v 7.2.2` pin (plan-checker warning resolved before execution)

## Deviations from Plan

None.

## Verification Evidence

```
$ ~/.dotnet/dotnet test KargoNet.sln --filter TopologyTests
Passed!  - Failed: 0, Passed: 5, Skipped: 0
```

---
*Phase: 02-contracts-messaging*
*Completed: 2026-11-19*

## Self-Check: PASSED
