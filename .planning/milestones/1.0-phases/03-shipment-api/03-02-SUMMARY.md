---
phase: 03-shipment-api
plan: 02
subsystem: api
tags: [minimal-api, transactions, outbox, integration-testing, msctesthosting]

# Dependency graph
requires: [03-01]
provides:
  - "POST /api/shipments — creates Shipment (Created) + outbox shipment.created row in one transaction; 409 on duplicate tracking number"
  - "GET /api/shipments (CreatedAt order) and GET /api/shipments/{trackingNumber} (404 when absent)"
  - "PUT /api/shipments/{id}/status — validates status name (400 invalid, 404 missing), updates Shipment + outbox shipment.status.changed row in one transaction"
  - "ShipmentDto / CreateShipmentRequest / UpdateStatusRequest DTOs"
affects: [03, 06]

actuals:
  tasks: 2
  commits: 1
  plan_head_before: 3f03108
  plan_head_after: 69636e9

tech-stack:
  added: [Microsoft.AspNetCore.Mvc.Testing 10.0.15, Microsoft.EntityFrameworkCore.InMemory 10.0.15 (service csproj), Microsoft.EntityFrameworkCore.Relational 10.0.15 (direct pin, both csprojs)]
  patterns:
    - "Endpoint writes domain row + outbox row in a single EF transaction (atomic outbox)"
    - "Outbox payload serialized with the same camelCase + enum-by-name options as Phase 2 EventSerializer"
    - "WebApplicationFactory tests drive the real Program.cs pipeline against a config-selected InMemory database"

key-files:
  created:
    - src/Services/Shipment.Api/Dto/ShipmentDto.cs
    - src/Services/Shipment.Api/Endpoints/Shipments.cs
    - tests/KargoNet.Tracking.Tests/Api/ShipmentEndpointTests.cs
  modified:
    - src/Services/Shipment.Api/Program.cs
    - src/Services/Shipment.Api/Shipment.Api.csproj
    - tests/KargoNet.Tracking.Tests/KargoNet.Tracking.Tests.csproj

key-decisions:
  - "Test database selected via Database:Provider/Database:Name configuration read in Program.cs (InMemory branch ignores TransactionIgnoredWarning) — test factory sets both with UseSetting before the entry point runs"
  - "PayloadOptions (camelCase + JsonStringEnumConverter) duplicated locally in Shipments.cs because EventSerializer.Options is private and the Phase 2 surface must not change"
  - "409 has a pre-check on the unique TrackingNumber plus a DbUpdateException backstop so concurrent duplicates still 409"

patterns-established:
  - "Every state-mutating endpoint writes the outbox row in the same transaction as the domain row — the publisher (03-03) never sees partial state"

requirements-completed: [SHIP-01, SHIP-02, SHIP-03]

coverage:
  - id: D1
    description: "POST /api/shipments creates Shipment + outbox row atomically; duplicate tracking number → 409"
    verification:
      - kind: integration
        ref: "ShipmentEndpointTests — create + outbox row assertions pass"
        status: pass
    human_judgment: false
  - id: D2
    description: "GET list/by-tracking-number and PUT status (400 invalid, 404 missing, outbox row on success)"
    verification:
      - kind: integration
        ref: "ShipmentEndpointTests — 6/6 pass"
        status: pass
    human_judgment: false

duration: 45min
completed: 2026-11-19
status: complete
---

# Phase 03 Plan 02: Shipment REST Endpoints + Atomic Outbox Writes Summary

**Minimal API endpoints (create/list/get/status) where every mutation writes the outbox row in the same EF transaction as the domain row, proven by 6 WebApplicationFactory integration tests over InMemory**

## Performance

- **Duration:** ~45 min
- **Tasks:** 2
- **Files modified:** 3 created, 3 modified

## Accomplishments

- `POST /api/shipments` — trim, 409 pre-check + `DbUpdateException` backstop, transaction: `Shipment(Created)` + outbox `shipment.created`
- `GET /api/shipments` (CreatedAt order) and `GET /api/shipments/{trackingNumber}` (404)
- `PUT /api/shipments/{id}/status` — 400 invalid name, 404 missing, transaction: `Status`/`UpdatedAt` + outbox `shipment.status.changed`
- `ShipmentDto`/`CreateShipmentRequest`/`UpdateStatusRequest` DTOs; outbox payloads serialized camelCase with enums by name (matching `EventSerializer`)
- `Program.cs` gained the `Database:Provider`/`Database:Name` switch so tests run the real pipeline against InMemory
- 6 integration tests via `WebApplicationFactory` (Mvc.Testing 10.0.15)

## Task Commits

1. **Task 1: DTOs + endpoints + Program.cs** - `69636e9` (part of plan commit)
2. **Task 2: Integration tests** - `69636e9` (part of plan commit)

## Files Created/Modified

- `src/Services/Shipment.Api/Dto/ShipmentDto.cs` — DTOs + `ShipmentDto.From(Shipment)`
- `src/Services/Shipment.Api/Endpoints/Shipments.cs` — `MapShipmentEndpoints`, per-endpoint transactions, local `PayloadOptions`
- `tests/KargoNet.Tracking.Tests/Api/ShipmentEndpointTests.cs` — 6 tests
- `src/Services/Shipment.Api/Program.cs` — `Database:Provider`/`Database:Name` InMemory switch
- `src/Services/Shipment.Api/Shipment.Api.csproj` — InMemory 10.0.15, Relational 10.0.15 direct pin
- `tests/KargoNet.Tracking.Tests/KargoNet.Tracking.Tests.csproj` — Mvc.Testing 10.0.15, InMemory + Relational 10.0.15, coverlet, ProjectReferences

## Decisions Made

- Test database via configuration switch in `Program.cs` — EF Core 10 `AddDbContext` composes all registered option actions onto one `DbContextOptions`, so a test-side DI swap re-registering a provider fails with "providers registered"; `ConfigureServices` in `ConfigureWebHost` runs before Program.cs registrations, and the remaining seams (`IHostBuilder` cast, `ConfigureHostBuilder`, `ServiceProviderFactory`) are not overridable/absent

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking issue] Microsoft.AspNetCore.Mvc.Testing 9.0.0 → 10.0.15**
- **Found during:** Task 2 (tests)
- **Issue:** 9.0.0 produced MSB3277 assembly-version conflicts against the net10 ASP.NET Core reference pack
- **Fix:** pinned Mvc.Testing 10.0.15 (plan permits "9.0.0 or resolving")
- **Files modified:** tests/KargoNet.Tracking.Tests/KargoNet.Tracking.Tests.csproj
- **Verification:** build 0/0, tests green
- **Committed in:** 69636e9

**2. [Rule 3 - Blocking issue] Direct Microsoft.EntityFrameworkCore.Relational 10.0.15 pin in BOTH csprojs**
- **Found during:** Task 2 (tests)
- **Issue:** Npgsql EF 10.0.3 and InMemory 10.0.15 both declare Relational `[10.0.4, 11.0.0)`; NuGet resolved the test graph to 10.0.4 while Shipment.Api.dll references 10.0.15 → MSB3277
- **Fix:** direct Relational 10.0.15 pin in Shipment.Api.csproj AND the test csproj (a pin in one project does not propagate to the other's restore)
- **Files modified:** both csprojs
- **Verification:** build 0/0
- **Committed in:** 69636e9

**3. [Rule 2/3 - Testability accommodation] InMemory switch in Program.cs + InMemory 10.0.15 in service csproj**
- **Found during:** Task 2 (tests)
- **Issue:** `Program.cs` hard-wired Npgsql, so `WebApplicationFactory` tests would require a live database; DI-swap routes were empirically dead (see Decisions)
- **Fix:** `Database:Provider`/`Database:Name` config switch in `Program.cs`; InMemory 10.0.15 added to Shipment.Api.csproj (Program.cs calls `UseInMemoryDatabase`); test factory sets both via `builder.UseSetting(...)`; `InMemoryEventId.TransactionIgnoredWarning` ignored in the InMemory branch (InMemory throws on `BeginTransactionAsync` otherwise)
- **Files modified:** Program.cs, Shipment.Api.csproj, ShipmentEndpointTests.cs
- **Verification:** 6/6 integration tests pass against the real pipeline
- **Committed in:** 69636e9

**4. [Rule 1 - Bug] Outbox payload elements were PascalCase**
- **Found during:** Task 2 (tests)
- **Issue:** default `JsonSerializer.SerializeToElement` produced PascalCase nested payload properties, but Phase 2 `EventSerializer` (and its tests) expect camelCase — the outbox bytes would not round-trip through `EventSerializer.Deserialize`
- **Fix:** local `PayloadOptions` (CamelCase + `JsonStringEnumConverter`) applied to both `SerializeToElement` calls; options duplicated because `EventSerializer.Options` is private and the Phase 2 surface must not change
- **Files modified:** Endpoints/Shipments.cs
- **Verification:** outbox rows asserted byte-exact against the Phase 2 wire format in tests
- **Committed in:** 69636e9

---

**Total deviations:** 4 auto-fixed (2 version pins, 1 testability accommodation, 1 bug)

## Verification Evidence

```
$ ~/.dotnet/dotnet build KargoNet.sln
0 Warning(s) 0 Error(s)

$ ~/.dotnet/dotnet test tests/KargoNet.Tracking.Tests/KargoNet.Tracking.Tests.csproj --filter "FullyQualifiedName~ShipmentEndpointTests"
Passed!  - Failed: 0, Passed: 6, Skipped: 0
```

---
*Phase: 03-shipment-api*
*Completed: 2026-11-19*

## Self-Check: PASSED
