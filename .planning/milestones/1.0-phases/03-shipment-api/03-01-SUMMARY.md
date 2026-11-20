---
phase: 03-shipment-api
plan: 01
subsystem: persistence
tags: [ef-core, npgsql, outbox, migrations, xunit]

# Dependency graph
requires: []
provides:
  - "Shipment + OutboxMessage entities with OutboxStatus enum in KargoNet.Shipment.Api.Entities"
  - "ShipmentDbContext (unique TrackingNumber index, (Status,CreatedAt) outbox index, string conversion for both Status props)"
  - "InitialCreate EF Core migration (design-time only — no live database)"
  - "ShipmentDbContextFactory for design-time tooling"
affects: [03, 06]

actuals:
  tasks: 2
  commits: 1
  plan_head_before: 9db2711
  plan_head_after: 3f03108

tech-stack:
  added: [Microsoft.EntityFrameworkCore 10.0.15, Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3, Microsoft.EntityFrameworkCore.Design 10.0.15, Microsoft.EntityFrameworkCore.InMemory (tests)]
  patterns:
    - "Hand-built outbox table (OutboxMessage) co-located with domain entities in the same DbContext"
    - "Enum-to-string HasConversion on both Status properties"

key-files:
  created:
    - src/Services/Shipment.Api/Entities/Shipment.cs
    - src/Services/Shipment.Api/Entities/OutboxMessage.cs
    - src/Services/Shipment.Api/Data/ShipmentDbContext.cs
    - src/Services/Shipment.Api/Data/ShipmentDbContextFactory.cs
    - src/Services/Shipment.Api/Migrations/20261119232650_InitialCreate.cs
    - src/Services/Shipment.Api/Migrations/20261119232650_InitialCreate.Designer.cs
    - src/Services/Shipment.Api/Migrations/ShipmentDbContextModelSnapshot.cs
    - tests/KargoNet.Tracking.Tests/Data/ShipmentDbContextTests.cs
  modified:
    - src/Services/Shipment.Api/Shipment.Api.csproj
    - tests/KargoNet.Tracking.Tests/KargoNet.Tracking.Tests.csproj

key-decisions:
  - "Both Status properties use HasConversion<string>() so enum names are stable in the DB and in outbox rows"
  - "Outbox index (Status, CreatedAt) supports the publisher's WHERE Status = Pending ORDER BY CreatedAt query"
  - "Migration generated design-time via ShipmentDbContextFactory — no live Postgres required"

patterns-established:
  - "Shipment.Api owns its DbContext; the outbox row is written in the same transaction as the shipment (proven in 03-02)"

requirements-completed: [SHIP-01]

coverage:
  - id: D1
    description: "Shipment/OutboxMessage entities match the locked schema; unique TrackingNumber enforced"
    verification:
      - kind: unit
        ref: "ShipmentDbContextTests — 2/2 pass (unique tracking number rejection, outbox index present in model)"
        status: pass
    human_judgment: false
  - id: D2
    description: "InitialCreate migration generated and applied to InMemory model snapshot"
    verification:
      - kind: unit
        ref: "dotnet test --filter ShipmentDbContextTests -> 2 passed, 0 failed; migration files committed"
        status: pass
    human_judgment: false

duration: 40min
completed: 2026-11-19
status: complete
---

# Phase 03 Plan 01: EF Core Persistence + InitialCreate Migration Summary

**Shipment/OutboxMessage entities, ShipmentDbContext with outbox index, and a design-time InitialCreate migration (EF Core 10.0.15 + Npgsql 10.0.3), proven by 2 model tests over InMemory**

## Performance

- **Duration:** ~40 min
- **Tasks:** 2
- **Files modified:** 8 created, 2 modified

## Accomplishments

- `Shipment` and `OutboxMessage` (+`OutboxStatus`) entities exactly as locked in 03-CONTEXT.md
- `ShipmentDbContext` — unique `TrackingNumber` index, `(Status, CreatedAt)` outbox index, `HasConversion<string>()` on both Status props
- `ShipmentDbContextFactory` + `InitialCreate` migration (20261119232650) generated with `dotnet-ef` — design-time only, no live database
- 2 unit tests over `UseInMemoryDatabase`: unique tracking number, outbox index in model

## Task Commits

1. **Task 1: Entities + DbContext** - `3f03108` (part of plan commit)
2. **Task 2: Migration + tests** - `3f03108` (part of plan commit)

## Files Created/Modified

- `src/Services/Shipment.Api/Entities/Shipment.cs` — domain entity
- `src/Services/Shipment.Api/Entities/OutboxMessage.cs` — outbox row + OutboxStatus
- `src/Services/Shipment.Api/Data/ShipmentDbContext.cs` — indexes + string enum conversion
- `src/Services/Shipment.Api/Data/ShipmentDbContextFactory.cs` — design-time factory
- `src/Services/Shipment.Api/Migrations/20261119232650_InitialCreate.cs` (+ Designer, ModelSnapshot)
- `tests/KargoNet.Tracking.Tests/Data/ShipmentDbContextTests.cs` — 2 tests
- `src/Services/Shipment.Api/Shipment.Api.csproj` — EF Core 10.0.15, Npgsql EF 10.0.3, Design 10.0.15
- `tests/KargoNet.Tracking.Tests/KargoNet.Tracking.Tests.csproj` — InMemory + ProjectReference

## Decisions Made

- Migration generated design-time only; live Postgres application is Phase 6 (compose stack)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking issue] dotnet-ef pinned to 10.0.15 instead of 11.0.0**
- **Found during:** Task 2 (migration)
- **Issue:** the latest `dotnet-ef` (11.x) apphost demands an uninstalled .NET 11 runtime; migration generation failed at launch
- **Fix:** installed `dotnet-ef` 10.0.15 global tool and invoked with `DOTNET_ROOT="$HOME/.dotnet"` + `PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"`
- **Files modified:** none (tooling only)
- **Verification:** migration generated and committed; build clean
- **Committed in:** 3f03108

---

**Total deviations:** 1 auto-fixed (tooling availability)

## Verification Evidence

```
$ ~/.dotnet/dotnet build KargoNet.sln
0 Warning(s) 0 Error(s)

$ ~/.dotnet/dotnet test tests/KargoNet.Tracking.Tests/KargoNet.Tracking.Tests.csproj --filter "FullyQualifiedName~ShipmentDbContextTests"
Passed!  - Failed: 0, Passed: 2, Skipped: 0
```

---
*Phase: 03-shipment-api*
*Completed: 2026-11-19*

## Self-Check: PASSED
