---
phase: 05-web-frontends
plan: 01
subsystem: customer-portal
tags: [razor, mvc, portal, xunit, mvc-testing]

# Dependency graph
requires: [02, 04]
provides:
  - "Portal.Web customer portal: `/` lookup, `/track` (found / not-found / no-signal states), `/shipments/new` create form, timeline rendering with shared rules 1–6"
  - "PortalApiClient over IHttpClientFactory (camelCase wire format, ApiRequestException on non-success)"
  - "tests/KargoNet.Portal.Tests — xunit project (Mvc.Testing 10.0.15) with FakePortalApiClient, controller tests for Home/Track/Shipments"
affects: [06]

actuals:
  tasks: 3
  commits: 3
  plan_head_before: 0bb0dec
  plan_head_after: 553d51a

tech-stack:
  added:
    - "Microsoft.AspNetCore.Mvc.Testing 10.0.15 (KargoNet.Portal.Tests, Private)"
    - "xunit 2.9.3 / Microsoft.NET.Test.Sdk 17.14.1 (pinned, mirrored from KargoNet.Tracking.Tests)"
  patterns:
    - "Controllers stay dumb: views render state, PortalApiClient is the single HTTP seam (injected via IPortalApiClient)"
    - "Timeline mapping in one private static method (BuildTimeline) implementing shared rules 1–6: occurredAt asc, created→sub-line, status-changed→badge, raw eventType fallback, payload parse in try/catch"
    - "Controller tests construct controllers directly with FakePortalApiClient — no WebApplicationFactory needed (thin controllers)"

key-files:
  created:
    - src/Web/Portal.Web/Controllers/TrackController.cs
    - src/Web/Portal.Web/Controllers/ShipmentsController.cs
    - src/Web/Portal.Web/Models/TrackingViewModel.cs
    - src/Web/Portal.Web/Models/TimelineItem.cs
    - src/Web/Portal.Web/Models/NewShipmentViewModel.cs
    - src/Web/Portal.Web/Services/IPortalApiClient.cs
    - src/Web/Portal.Web/Services/PortalApiClient.cs
    - src/Web/Portal.Web/Services/ApiRequestException.cs
    - src/Web/Portal.Web/Views/Track/Index.cshtml
    - src/Web/Portal.Web/Views/Shipments/New.cshtml
    - src/Web/Portal.Web/Views/Shared/_StatusBadge.cshtml
    - tests/KargoNet.Portal.Tests/KargoNet.Portal.Tests.csproj
    - tests/KargoNet.Portal.Tests/Fakes/FakePortalApiClient.cs
    - tests/KargoNet.Portal.Tests/Controllers/HomeControllerTests.cs
    - tests/KargoNet.Portal.Tests/Controllers/TrackControllerTests.cs
    - tests/KargoNet.Portal.Tests/Controllers/ShipmentsControllerTests.cs
  modified:
    - src/Web/Portal.Web/Program.cs
    - src/Web/Portal.Web/appsettings.json
    - src/Web/Portal.Web/Controllers/HomeController.cs
    - src/Web/Portal.Web/Views/Home/Index.cshtml
    - src/Web/Portal.Web/Views/Home/Privacy.cshtml
    - src/Web/Portal.Web/Views/Shared/_Layout.cshtml
    - src/Web/Portal.Web/wwwroot/css/site.css
    - KargoNet.sln

key-decisions:
  - "`TimelineItem` carries `Label` / `Status` / `SubLine` / `OccurredAt` — the `SubLine` (description / destination for `shipment.created`) is required by the UI spec even though the plan listed only Label/Status/OccurredAt"
  - "Explicit `Response.StatusCode = StatusCodes.Status400BadRequest` in `ShipmentsController` POST: .NET 10 does NOT auto-400 on invalid ModelState with `View(model)`"
  - "Controller tests instantiate controllers directly with `FakePortalApiClient` (no `WebApplicationFactory`) — the controllers are thin view-model mappers, and this keeps the test project free of host-startup coupling"
  - "API base URLs come from `appsettings.json` (`ShipmentApiUrl` / `TrackingApiUrl`) injected via configuration — no hardcoded URLs in controllers"

patterns-established:
  - "Portal state triad per screen: loading / data / error-with-Retry, rendered with `@if/@else if` in cshtml; timeline rows identical to operator drawer O2 (shared rules 1–6)"
  - "`ApiRequestException` (message = user-facing reason) is the single failure channel from `PortalApiClient`; controllers map it to the error state, never to 500"

requirements-completed: [CUST-01, CUST-02, CUST-03]

duration: 120min
completed: 2026-11-20
status: complete
---

# Phase 05 Plan 01: Customer Portal Web Summary

**Razor customer portal with lookup, tracking timeline (shared rules 1–6), and create-shipment form, backed by `PortalApiClient` and proven by 12 controller tests over `FakePortalApiClient`**

## Performance

- **Duration:** ~120 min
- **Tasks:** 3
- **Files modified:** 16 created, 8 modified

## Accomplishments

- `Portal.Web` portal: `/` home lookup form, `/track` (found state with timeline, not-found, no-signal/API-down error with Retry), `/shipments/new` (create form with server-side validation, redirect to `/track` on success)
- `PortalApiClient` over `IHttpClientFactory` with camelCase wire format; `ApiRequestException` as the single failure seam
- `TrackController.BuildTimeline` implementing the shared timeline rules 1–6 (occurredAt ascending; `shipment.created` → `Shipment created` + `description / destination` sub-line; `shipment.status.changed` → `Status changed to {status}` + badge; unknown types → raw `eventType`; payload parse guarded)
- `tests/KargoNet.Portal.Tests` — xunit project (pins mirrored from `KargoNet.Tracking.Tests`, Mvc.Testing 10.0.15), `FakePortalApiClient`, 12 tests across Home/Track/Shipments controllers
- Full solution: build 0 errors, 90/90 tests green (78 Tracking + 12 Portal)

## Task Commits

1. **Task 1: Portal tracer — /track found state** - `d2fc688`
2. **Task 2: Create-shipment form /shipments/new + home lookup** - `6e1f084`
3. **Task 3: All track states + full suite** - `553d51a`

## Files Created/Modified

- `src/Web/Portal.Web/Controllers/{TrackController,ShipmentsController}.cs` — timeline mapping + create form
- `src/Web/Portal.Web/Models/{TrackingViewModel,TimelineItem,NewShipmentViewModel}.cs`
- `src/Web/Portal.Web/Services/{IPortalApiClient,PortalApiClient,ApiRequestException}.cs`
- `src/Web/Portal.Web/Views/Track/Index.cshtml`, `Views/Shipments/New.cshtml`, `Views/Shared/_StatusBadge.cshtml` — new screens
- `src/Web/Portal.Web/Views/{Home/Index,Home/Privacy,Shared/_Layout}.cshtml`, `wwwroot/css/site.css` — portal shell + status badge styles
- `src/Web/Portal.Web/{Program.cs,appsettings.json}` — DI + API URL config
- `tests/KargoNet.Portal.Tests/*` — csproj, `FakePortalApiClient`, 3 controller test classes (12 tests)
- `KargoNet.sln` — test project added

## Decisions Made

- `TimelineItem.SubLine` added beyond the plan's Label/Status/OccurredAt — the UI spec's shared rules require a description/destination sub-line for `shipment.created`
- Explicit `StatusCodes.Status400BadRequest` on invalid `NewShipmentViewModel`: .NET 10 does not auto-400 on invalid ModelState with `View(model)` (and the member is named `Status400BadRequest`, not `StatusBadRequest`)
- Controller tests construct controllers directly with `FakePortalApiClient` — no `WebApplicationFactory`; the controllers are thin view-model mappers
- Test `HttpClient` (Mvc.Testing) auto-follows 302 redirects: `New_PostValid_RedirectsToTrack` follows the redirect and asserts on the landed `/track` page

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing functionality] `TimelineItem.SubLine` added**
- **Found during:** Task 1 (timeline rendering)
- **Issue:** The plan's `TimelineItem` listed `Label` / `Status` / `OccurredAt`, but the approved UI spec's shared timeline rule 2 requires a `description / destination` sub-line for `shipment.created`
- **Fix:** added `SubLine` property; `BuildTimeline` populates it from `payloadJson`
- **Files modified:** src/Web/Portal.Web/Models/TimelineItem.cs, src/Web/Portal.Web/Controllers/TrackController.cs
- **Committed in:** d2fc688

**2. [Rule 1 - Bug] POST /shipments/new did not return 400 on invalid ModelState**
- **Found during:** Task 2 (create-form tests)
- **Issue:** .NET 10 does not auto-400 on invalid ModelState with `View(model)`; the endpoint returned 200 with a re-rendered form
- **Fix:** explicit `Response.StatusCode = StatusCodes.Status400BadRequest` in `ShipmentsController` POST on `!ModelState.IsValid`
- **Files modified:** src/Web/Portal.Web/Controllers/ShipmentsController.cs
- **Committed in:** 6e1f084

**3. [Rule 1 - Bug] Test assertions failed on apostrophes**
- **Found during:** Task 3 (states tests)
- **Issue:** Razor HTML-encodes `'` as `&#39;` in output, so exact-string assertions containing apostrophes failed
- **Fix:** test assertions use apostrophe-free substrings
- **Files modified:** tests/KargoNet.Portal.Tests/Controllers/TrackControllerTests.cs
- **Committed in:** 553d51a

**4. [Rule 1 - Bug] Success-redirect test hit the raw 302**
- **Found during:** Task 2
- **Issue:** the test `HttpClient` auto-follows 302s, so the test cannot assert on the Location header
- **Fix:** `New_PostValid_RedirectsToTrack` follows the redirect and asserts on the landed `/track` page content
- **Files modified:** tests/KargoNet.Portal.Tests/Controllers/ShipmentsControllerTests.cs
- **Committed in:** 6e1f084

---

**Total deviations:** 4 auto-fixed (1 missing functionality, 3 test/behavior bugs)

## Verification Evidence

```
$ ~/.dotnet/dotnet build KargoNet.sln
0 Error(s)

$ ~/.dotnet/dotnet test KargoNet.sln
Passed!  - Failed: 0, Passed: 78, Skipped: 0, Total: 78  (KargoNet.Tracking.Tests)
Passed!  - Failed: 0, Passed: 12, Skipped: 0, Total: 12  (KargoNet.Portal.Tests)
```

---
*Phase: 05-web-frontends*
*Completed: 2026-11-20*

## Self-Check: PASSED
