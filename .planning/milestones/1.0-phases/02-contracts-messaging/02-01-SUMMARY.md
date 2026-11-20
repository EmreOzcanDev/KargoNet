---
phase: 02-contracts-messaging
plan: 01
subsystem: contracts
tags: [event-envelope, system-text-json, serialization, xunit]

# Dependency graph
requires: []
provides:
  - "EventEnvelope { EventId, EventType, OccurredAt, Payload } in KargoNet.Contracts.Events"
  - "ShipmentCreatedPayload { TrackingNumber, Description, Destination } and ShipmentStatusChangedPayload { ShipmentId, TrackingNumber, FromStatus, ToStatus }"
  - "EventSerializer (System.Text.Json, camelCase) with serialize/deserialize round-trip"
affects: [02, 03, 04, 05, 06]

actuals:
  tasks: 2
  commits: 1
  plan_head_before: 98c7a4d
  plan_head_after: 67cf770

tech-stack:
  added: [System.Text.Json (built-in)]
  patterns:
    - "Envelope record + typed payload records in Contracts.Events"
    - "camelCase wire format owned by a single EventSerializer"

key-files:
  created:
    - src/BuildingBlocks/KargoNet.Contracts/Events/EventEnvelope.cs
    - src/BuildingBlocks/KargoNet.Contracts/Events/EventSerializer.cs
    - src/BuildingBlocks/KargoNet.Contracts/Events/ShipmentCreatedPayload.cs
    - src/BuildingBlocks/KargoNet.Contracts/Events/ShipmentStatusChangedPayload.cs
    - tests/Tracking.Tests/Contracts/EventSerializerTests.cs
  modified: []

key-decisions:
  - "JsonElement? Payload keeps the envelope type-agnostic; EventSerializer maps to/from the two payload records by EventType"
  - "JsonDocument.Parse(byte[]) one bounded copy — net10 JsonDocument has no ReadOnlySpan<byte> overload"

patterns-established:
  - "Event contracts live in KargoNet.Contracts.Events; consumers in phases 3–6 depend on these exact type names"

requirements-completed: [MSG-02, MSG-03, MSG-04]

coverage:
  - id: D1
    description: "Envelope + payload records exist with the locked field names"
    verification:
      - kind: unit
        ref: "EventSerializerTests — 4/4 pass (round-trip, camelCase wire names, status-by-name)"
        status: pass
    human_judgment: false
  - id: D2
    description: "EventSerializer round-trips both payload types with camelCase JSON"
    verification:
      - kind: unit
        ref: "dotnet test --filter EventSerializerTests -> 4 passed, 0 failed"
        status: pass
    human_judgment: false

duration: 25min
completed: 2026-11-19
status: complete
---

# Phase 02 Plan 01: Event Contracts Summary

**EventEnvelope + ShipmentCreated/ShipmentStatusChanged payload records with a camelCase System.Text.Json EventSerializer, proven by 4 round-trip unit tests**

## Performance

- **Duration:** ~25 min
- **Tasks:** 2
- **Files modified:** 5 created

## Accomplishments

- `EventEnvelope { EventId, EventType, OccurredAt, JsonElement? Payload }` exactly as locked in 02-CONTEXT.md
- Both payload records with locked field names
- `EventSerializer` — serialize/deserialize via System.Text.Json, camelCase, no third-party dependency
- 4 unit tests: round-trip for both payload types, camelCase wire-name assertions, status-by-name

## Task Commits

1. **Task 1: Event records** - `67cf770` (part of plan commit)
2. **Task 2: EventSerializer + tests** - `67cf770` (part of plan commit)

## Files Created/Modified

- `src/BuildingBlocks/KargoNet.Contracts/Events/EventEnvelope.cs` — envelope record
- `src/BuildingBlocks/KargoNet.Contracts/Events/EventSerializer.cs` — camelCase System.Text.Json round-trip
- `src/BuildingBlocks/KargoNet.Contracts/Events/ShipmentCreatedPayload.cs` — TrackingNumber, Description, Destination
- `src/BuildingBlocks/KargoNet.Contracts/Events/ShipmentStatusChangedPayload.cs` — ShipmentId, TrackingNumber, FromStatus, ToStatus
- `tests/Tracking.Tests/Contracts/EventSerializerTests.cs` — 4 tests

## Decisions Made

- `JsonDocument.Parse(utf8.ToArray())` — net10 `JsonDocument` has no `ReadOnlySpan<byte>` overload; one bounded copy, accepted

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - API availability] JsonDocument span overload missing**
- **Found during:** Task 2 (serializer)
- **Issue:** `JsonDocument.Parse(ReadOnlySpan<byte>)` does not exist on net10
- **Fix:** `JsonDocument.Parse(utf8.ToArray())` — single bounded copy
- **Files modified:** EventSerializer.cs
- **Verification:** tests pass, no behavior change
- **Committed in:** 67cf770

---

**Total deviations:** 1 auto-fixed (API availability)

## Verification Evidence

```
$ ~/.dotnet/dotnet test KargoNet.sln --filter EventSerializerTests
Passed!  - Failed: 0, Passed: 4, Skipped: 0
```

---
*Phase: 02-contracts-messaging*
*Completed: 2026-11-19*

## Self-Check: PASSED
