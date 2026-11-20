---
phase: 05-web-frontends
plan: 02
subsystem: operator-console
tags: [angular, signalr, zoneless, vitest, http-testing]

# Dependency graph
requires: [02, 04]
provides:
  - "operator-ui Angular 22 SPA (zoneless, standalone): shipments table with live row patches, status Apply (PUT) with per-row inline error, connection indicator, timeline drawer (join/leave tracking group + live re-fetch)"
  - "Core services: ShipmentApiService, TrackingApiService, TrackingService (persistent SignalR hub connection, `shipmentUpdated` / `connectionState` BehaviorSubjects, group join/leave)"
  - "Environment config (`src/environments/environment.ts`) — the only place API URLs appear"
affects: [06]

actuals:
  tasks: 3
  commits: 3
  plan_head_before: 553d51a
  plan_head_after: f6573ca

tech-stack:
  added:
    - "@microsoft/signalr 10.0.11 (first-party, verified via `npm ls`)"
    - "vitest / @angular/build unit-test runner (already in package.json; `npm test -- --watch=false`)"
  patterns:
    - "Zoneless change detection: every state mutation from an RxJS `subscribe` callback is followed by `this.cd.markForCheck()` — plain mutations do NOT re-render in the zoneless vitest env"
    - "One persistent hub connection for the app (`TrackingService`); drawers/rows only join/leave groups; `ShipmentUpdated` filtered per component"
    - "Row identity by `trackingNumber` with a `rowMeta` Map (select value, apply error, disabled state) — live patches update rows in place, unknown numbers prepend a row resolved via `getShipment`"
    - "Specs: `HttpTestingController` (flush with `{ status, statusText }` object form), `FakeTrackingService` overriding `createConnection()`, `http.verify()` in every `afterEach`"

key-files:
  created:
    - src/Web/operator-ui/src/environments/environment.ts
    - src/Web/operator-ui/src/app/core/models.ts
    - src/Web/operator-ui/src/app/core/statuses.ts
    - src/Web/operator-ui/src/app/core/statuses.spec.ts
    - src/Web/operator-ui/src/app/core/shipment-api.service.ts
    - src/Web/operator-ui/src/app/core/tracking-api.service.ts
    - src/Web/operator-ui/src/app/core/tracking.service.ts
    - src/Web/operator-ui/src/app/core/tracking.service.spec.ts
    - src/Web/operator-ui/src/app/shipments/shipments.ts
    - src/Web/operator-ui/src/app/shipments/shipments.html
    - src/Web/operator-ui/src/app/shipments/shipments.scss
    - src/Web/operator-ui/src/app/shipments/shipments.spec.ts
    - src/Web/operator-ui/src/app/shipments/timeline-drawer.ts
    - src/Web/operator-ui/src/app/shipments/timeline-drawer.html
    - src/Web/operator-ui/src/app/shipments/timeline-drawer.scss
    - src/Web/operator-ui/src/app/shipments/timeline-drawer.spec.ts
  modified:
    - src/Web/operator-ui/package.json
    - src/Web/operator-ui/src/styles.scss
    - src/Web/operator-ui/src/index.html
    - src/Web/operator-ui/src/app/app.config.ts
    - src/Web/operator-ui/src/app/app.routes.ts
    - src/Web/operator-ui/src/app/app.html
    - src/Web/operator-ui/src/app/app.spec.ts

key-decisions:
  - "Zoneless `markForCheck()` discipline: verified empirically — the Angular 22 vitest env only re-renders `markForCheck()`ed views; `fixture.detectChanges()` alone is a no-op for unchanged-since-schedule views, so every subscribe callback ends with `this.cd.markForCheck()`"
  - "`TrackingService` is the single hub owner: idempotent `start()`, idempotent `dispose()`, `joinGroup`/`leaveGroup` map to `JoinTrackingGroup`/`LeaveTrackingGroup`; components subscribe to `shipmentUpdated` and filter by tracking number"
  - "Host owns single open-drawer state (`openNumber: string | null`); the drawer's `@Input` setter handles in-place number changes (leave old group, join new) and `closeDrawer()` is idempotent so `ngOnDestroy` double-calls don't double-leave — one group active at a time"
  - "Drawer timeline mapping is a module-level `mapEvents()` implementing shared rules 1–6, structurally identical to the portal's `BuildTimeline`"
  - "API URLs live only in `src/environments/environment.ts` (verified: no `localhost:5` anywhere under `src/app/`)"

patterns-established:
  - "Angular 22 test API shapes (empirically confirmed): `http.flush(body, { status, statusText })` object form; no `openRequests()`; `ngOnInit` fires on the FIRST `detectChanges`; `ng test` has no `-t` flag (filter via `--include`); `connection.on('Event', (dto: Type) => ...)` — no generic in SignalR v10 typings"
  - "Per-row inline error state (`applyError`) survives re-renders via the `rowMeta` Map keyed by tracking number"

requirements-completed: [OPS-01, OPS-02, OPS-03, OPS-04]

duration: 150min
completed: 2026-11-20
status: complete
---

# Phase 05 Plan 02: Operator Console (Angular SPA) Summary

**Zoneless Angular 22 operator console: live shipments table, status Apply with per-row inline errors, connection indicator, and a timeline drawer with SignalR group join/leave + live re-fetch — 25 vitest unit tests green**

## Performance

- **Duration:** ~150 min
- **Tasks:** 3
- **Files modified:** 16 created, 7 modified

## Accomplishments

- `operator-ui` SPA: `ShipmentsComponent` (loading/empty/error/populated states, exact spec copy), `TrackingService` (persistent hub connection, `Live`/`Reconnecting…`/`Offline` indicator), status `Apply` (PUT with per-row inline error + selection preserved), `TimelineDrawerComponent` (join group on open, `ShipmentUpdated` re-fetch while open, leave group on close, empty/error/Retry states, shared timeline rules 1–6)
- Core services `ShipmentApiService` / `TrackingApiService` over `HttpClient`; environments file as the only URL source
- 25 vitest tests: app (2), statuses (2), tracking service (7), shipments (8), timeline drawer (6)
- Final gates: `npm run build` OK, 25/25 tests, no `innerHTML`, no hardcoded `localhost:5` under `src/app/`, `@microsoft/signalr@10.0.11` first-party

## Task Commits

1. **Task 1: Shipments table tracer — environment, core services, populated state** - `87f088a`
2. **Task 2: Status Apply + live hub updates with indicator** - `e5b9d4d`
3. **Task 3: Timeline drawer with group join/leave + live re-fetch** - `f6573ca`

## Files Created/Modified

- `src/Web/operator-ui/src/environments/environment.ts` — `shipmentApiUrl` / `trackingApiUrl`
- `src/Web/operator-ui/src/app/core/{models,statuses,shipment-api.service,tracking-api.service,tracking.service}.ts` (+ specs)
- `src/Web/operator-ui/src/app/shipments/{shipments.ts,shipments.html,shipments.scss,shipments.spec.ts}`
- `src/Web/operator-ui/src/app/shipments/timeline-drawer.{ts,html,scss,spec.ts}`
- `src/Web/operator-ui/src/{styles.scss,index.html}`, `src/app/{app.config.ts,app.routes.ts,app.html,app.spec.ts}`
- `src/Web/operator-ui/package.json` — `@microsoft/signalr@^10`

## Decisions Made

- Zoneless `markForCheck()` in every subscribe callback — empirically required; without it the vitest env (and production zoneless) never re-renders RxJS-driven state
- Single hub connection owned by `TrackingService`; drawers only join/leave groups (D4); `dispose()` is idempotent and nulls the connection first
- Drawer `closeDrawer()` idempotent (guards `_trackingNumber !== ''`) so `ngOnDestroy` after host removal doesn't double-leave
- Drawer error copy: `Couldn't load the timeline — try again.` with `Retry` (UI spec requires an inline alert with Retry; copy chosen for consistency with the table's O1 banner tone)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Zoneless change detection — RxJS state not re-rendered**
- **Found during:** Task 2 (live-update tests red: DOM never reflected `shipmentUpdated` patches)
- **Issue:** in the zoneless Angular 22 vitest env, only `markForCheck()`ed views re-render; plain state mutation in a `subscribe` callback is a no-op
- **Fix:** `this.cd.markForCheck()` appended to every subscribe callback in `ShipmentsComponent`, `TrackingService` consumers, and `TimelineDrawerComponent`
- **Files modified:** src/Web/operator-ui/src/app/shipments/shipments.ts, src/Web/operator-ui/src/app/shipments/timeline-drawer.ts
- **Committed in:** e5b9d4d (shipments), f6573ca (drawer)

**2. [Rule 1 - Bug] SignalR v10 `connection.on()` takes no generic**
- **Found during:** Task 2 (TS2558 on `connection.on<TrackingUpdatedDto>(...)`)
- **Issue:** v10 typings have no type argument for `on`
- **Fix:** annotated the handler parameter: `connection.on('ShipmentUpdated', (dto: TrackingUpdatedDto) => ...)`
- **Files modified:** src/Web/operator-ui/src/app/core/tracking.service.ts
- **Committed in:** e5b9d4d

**3. [Rule 1 - Bug] Apply spec clicked the wrong target / red on select change**
- **Found during:** Task 2 (Apply tests red)
- **Issue:** the `<select>` `(change)` only updates the selection; the PUT fires from the `Apply` button click — the spec must click the button
- **Fix:** spec clicks the `Apply` button; `http.flush(body, { status: 500, statusText })` object form used for the 500 case
- **Files modified:** src/Web/operator-ui/src/app/shipments/shipments.spec.ts
- **Committed in:** e5b9d4d

**4. [Rule 3 - Blocking issue] TS1029 modifier order in spec**
- **Found during:** Task 2
- **Issue:** `override protected` is rejected by TS (TS1029)
- **Fix:** `protected override` order in `FakeTrackingService`/spec overrides
- **Files modified:** src/Web/operator-ui/src/app/core/tracking.service.spec.ts
- **Committed in:** e5b9d4d

**5. [Rule 1 - Bug] Pre-existing Apply spec selectors broke with the new tracking-link button**
- **Found during:** Task 3 (3 shipments tests red — `tbody tr button` now matched the tracking-link button)
- **Issue:** the host wiring added a link-styled button before `Apply` in each row
- **Fix:** spec finds the `Apply` button by text; also corrected the row template (restored the `description || '—'` cell) during host wiring
- **Files modified:** src/Web/operator-ui/src/app/shipments/shipments.spec.ts, src/Web/operator-ui/src/app/shipments/shipments.html
- **Committed in:** f6573ca

---

**Total deviations:** 5 auto-fixed (4 bugs, 1 blocking TS issue)

## Verification Evidence

```
$ cd src/Web/operator-ui && npm run build
✔ Building... (0 errors)

$ cd src/Web/operator-ui && npm test -- --watch=false
Test Files  5 passed (5)
     Tests  25 passed (25)

$ cd src/Web/operator-ui && grep -rn "innerHTML" src/app/   → (empty)
$ cd src/Web/operator-ui && grep -rn "localhost:5" src/app/ → (empty)
$ cd src/Web/operator-ui && npm ls @microsoft/signalr
└── @microsoft/signalr@10.0.11

$ ~/.dotnet/dotnet build KargoNet.sln   → 0 Error(s)
$ ~/.dotnet/dotnet test KargoNet.sln
Passed!  - Failed: 0, Passed: 78 (KargoNet.Tracking.Tests)
Passed!  - Failed: 0, Passed: 12 (KargoNet.Portal.Tests)
```

---
*Phase: 05-web-frontends*
*Completed: 2026-11-20*

## Self-Check: PASSED
