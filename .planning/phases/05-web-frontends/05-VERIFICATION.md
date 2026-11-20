---
phase: 05-web-frontends
verified: 2026-11-20T09:39:19Z
status: passed
score: 5/5
behavior_unverified: 0
overrides_applied: 0
covered_files:
  - ".planning/phases/05-web-frontends/05-01-PLAN.md"
  - ".planning/phases/05-web-frontends/05-01-SUMMARY.md"
  - ".planning/phases/05-web-frontends/05-02-PLAN.md"
  - ".planning/phases/05-web-frontends/05-02-SUMMARY.md"
  - "KargoNet.sln"
  - "src/Web/Portal.Web/Controllers/HomeController.cs"
  - "src/Web/Portal.Web/Controllers/ShipmentsController.cs"
  - "src/Web/Portal.Web/Controllers/TrackController.cs"
  - "src/Web/Portal.Web/Models/NewShipmentViewModel.cs"
  - "src/Web/Portal.Web/Models/TimelineItem.cs"
  - "src/Web/Portal.Web/Models/TrackingViewModel.cs"
  - "src/Web/Portal.Web/Program.cs"
  - "src/Web/Portal.Web/Services/ApiRequestException.cs"
  - "src/Web/Portal.Web/Services/IPortalApiClient.cs"
  - "src/Web/Portal.Web/Services/PortalApiClient.cs"
  - "src/Web/Portal.Web/Services/PortalDtos.cs"
  - "src/Web/Portal.Web/Views/Home/Index.cshtml"
  - "src/Web/Portal.Web/Views/Shared/_Layout.cshtml"
  - "src/Web/Portal.Web/Views/Shared/_StatusBadge.cshtml"
  - "src/Web/Portal.Web/Views/Shipments/New.cshtml"
  - "src/Web/Portal.Web/Views/Track/Index.cshtml"
  - "src/Web/Portal.Web/appsettings.json"
  - "src/Web/Portal.Web/wwwroot/css/site.css"
  - "src/Web/operator-ui/package-lock.json"
  - "src/Web/operator-ui/package.json"
  - "src/Web/operator-ui/src/app/app.config.ts"
  - "src/Web/operator-ui/src/app/app.html"
  - "src/Web/operator-ui/src/app/app.routes.ts"
  - "src/Web/operator-ui/src/app/app.spec.ts"
  - "src/Web/operator-ui/src/app/core/models.ts"
  - "src/Web/operator-ui/src/app/core/shipment-api.service.ts"
  - "src/Web/operator-ui/src/app/core/statuses.spec.ts"
  - "src/Web/operator-ui/src/app/core/statuses.ts"
  - "src/Web/operator-ui/src/app/core/tracking-api.service.ts"
  - "src/Web/operator-ui/src/app/core/tracking.service.spec.ts"
  - "src/Web/operator-ui/src/app/core/tracking.service.ts"
  - "src/Web/operator-ui/src/app/shipments/shipments.html"
  - "src/Web/operator-ui/src/app/shipments/shipments.scss"
  - "src/Web/operator-ui/src/app/shipments/shipments.spec.ts"
  - "src/Web/operator-ui/src/app/shipments/shipments.ts"
  - "src/Web/operator-ui/src/app/shipments/timeline-drawer.html"
  - "src/Web/operator-ui/src/app/shipments/timeline-drawer.scss"
  - "src/Web/operator-ui/src/app/shipments/timeline-drawer.spec.ts"
  - "src/Web/operator-ui/src/app/shipments/timeline-drawer.ts"
  - "src/Web/operator-ui/src/environments/environment.ts"
  - "src/Web/operator-ui/src/styles.scss"
  - "tests/KargoNet.Portal.Tests/Controllers/HomeControllerTests.cs"
  - "tests/KargoNet.Portal.Tests/Controllers/ShipmentsControllerTests.cs"
  - "tests/KargoNet.Portal.Tests/Controllers/TrackControllerTests.cs"
  - "tests/KargoNet.Portal.Tests/Fakes/FakePortalApiClient.cs"
  - "tests/KargoNet.Portal.Tests/KargoNet.Portal.Tests.csproj"
  - "tests/KargoNet.Portal.Tests/PortalFactory.cs"
covered_digest: "v4:sha256:d299960c24f53fda9a21f9499f3e3a27ebf181c5ee5c38b9ff6d7ee2301ff710"
---
## Goal Achievement
**Phase goal (ROADMAP.md):** Customer portal (ASP.NET MVC, Razor) + operator dashboard (Angular 21, standalone, SignalR) — create shipments, track with full timeline, operator status updates, live updates.
### Observable Truths (Success Criteria)
| # | SC (ROADMAP.md) | Status | Evidence |
|---|---|---|---|
| 1 | Shipment creation via portal form | ✅ VERIFIED | `ShipmentsController.New` (`src/Web/Portal.Web/Controllers/ShipmentsController.cs:17-45`): GET renders form, POST is `[ValidateAntiForgeryToken]`, invalid model → 400 + validation summary, success → `Redirect("/track?number=…")` with `Uri.EscapeDataString` (line 40). `PortalApiClient.CreateShipmentAsync` (`Services/PortalApiClient.cs:62-83`) POSTs to `api/shipments` (URL per D7). `Views/Shipments/New.cshtml` — optional tracking number ("Leave blank to auto-generate"), description, required destination, dismissible error banner (lines 10-16, 20-41). Home link: `Views/Home/Index.cshtml` (`asp-controller="Shipments" asp-action="New"`). Tests: `New_RendersFormWithFieldsAndHint`, `New_PostWithEmptyDestination_ReturnsBadRequestWithValidationMessage`, `New_PostValid_RedirectsToTrack`, `New_PostWhenApiFails_RendersErrorBanner` (4/4 in `ShipmentsControllerTests.cs`). |
| 2 | Search by number → current status + full timeline | ✅ VERIFIED | `TrackController.Index` (`Controllers/TrackController.cs:21-73`): missing number → empty state; 404 → not-found "No shipment found for `{n}`"; success → `TrackingViewModel` with status card; API failure → error state. `PortalApiClient.GetTrackingAsync` (404 → null, `Services/PortalApiClient.cs:32-50`). `Views/Track/Index.cshtml` — status card with `_StatusBadge` partial (7/7 status→Bootstrap-class map, unknown → neutral), timeline list (lines 53-86). Tests: `Track_Found_RendersShipmentCardAndTimeline`, `Track_NoNumber_RendersEmptyState`, `Track_UnknownNumber_RendersNotFound`, `Track_ApiFails_RendersErrorState`. |
| 3 | Timeline shows full history (created → each status change) | ✅ VERIFIED | `BuildTimeline` (`Controllers/TrackController.cs:75-128`) — shared rules 1-6: `occurredAt` ascending, `shipment.created` → "Shipment created" + `description / destination` sub-line, `shipment.status.changed` → "Status changed to {status}" + status badge, unknown type → raw `eventType` label, `JsonException` guard on unparseable payloads. Mirror implementation in operator-ui: `timeline-drawer.ts:35-76` (`mapEvents`, same rules). Tests: `Track_SingleCreatedEvent_RendersOneRow`, `Track_WeirdEvent_RendersRawEventTypeLabel`, `Track_TimelineFails_RendersCardWithTimelineError`; drawer: "renders the eventType text when payloadJson is invalid". |
| 4 | Operator table of all shipments with live status badges | ✅ VERIFIED | `src/Web/operator-ui/src/app/shipments/shipments.html:23-81` — table (Tracking number / Description / Destination / Status / Updated / Actions), badge `[class]="badgeClass[row.status]"` (line 45) fed by `core/statuses.ts` (`STATUS_OPTIONS` 7 values + `STATUS_BADGE_CLASS` map, D6 — single source of truth). `ShipmentsComponent.load()` (`shipments.ts:79-94`) fetches `GET {shipmentApiUrl}/api/shipments` via `ShipmentApiService.listShipments`. Loading/empty/error/populated states + spec-copy banner (`shipments.html:19`: "We couldn't reach the KargoNet service. Check your connection and try again." — matches UI-SPEC O1). Tests: "renders the empty state for an empty list", "renders rows with badge classes for 2 shipments", "renders the error banner and Retry re-issues the list fetch", `statuses.spec.ts` (7 values + full badge map). |
| 5 | Operator status updates through the lifecycle | ✅ VERIFIED | `shipments.ts:110-134` — per-row `<select>` (7 options, preselected via `selectFor()` falling back to `row.status`, M1 fix) + `applyStatus` → `ShipmentApiService.updateStatus` → `PUT {shipmentApiUrl}/api/shipments/{id}/status` with `encodeURIComponent(id)` (`core/shipment-api.service.ts:23`). On 200: row fields + select updated locally; on error: inline "Couldn't update status — try again." (`shipments.html:73-75`) with selection kept. Tests: "Apply with a 200 response patches the row badge", "Apply with a 500 response shows the inline row error and keeps the selection", "keeps the status select preselected to the row status across a passive CD pass" (M1 regression spec). |
| 6 | Live SignalR updates without refresh (SC5) | ✅ VERIFIED | `core/tracking.service.ts` — single persistent hub connection to `{trackingApiUrl}/hubs/tracking` (D4): idempotent `start()`/`dispose()` (N1 `.catch(() => {})` guards on `start`/`invoke`, lines 64-74), `ShipmentUpdated` → `shipmentUpdated` BehaviorSubject (null-tracking-number payloads dropped), `stateChanged` → `connectionState` (Live / Reconnecting… / Offline). `shipments.ts:136-181` — `onShipmentUpdated` patches the matching row in place (badge, updatedAt, select) or `prependRow`s a new one (top, select disabled until `getShipment` resolves the id — O1). `shipments.html:4` indicator. Tests: `tracking.service.spec.ts` (7 specs: payload delivery, null-drop, state mapping, join/leave, dispose idempotency, start idempotency, initial state), "ShipmentUpdated for an existing row patches it in place", "ShipmentUpdated for a new tracking number prepends a row and resolves its id", "the indicator reflects the three connection states". |
| 7 | Timeline drawer with full history (SC5) | ✅ VERIFIED | `shipments/timeline-drawer.ts` — `set trackingNumber` (101-118): joins hub group, subscribes (N2: prior subscription unsubscribed first, line 110), fetches `GET {trackingApiUrl}/api/tracking/{n}/events`; `ShipmentUpdated` for the open number re-fetches (live, D5); `closeDrawer()` (128-136) leaves the group + unsubscribes. Drawer HTML (`timeline-drawer.html`) — loading/empty/error states with spec copy, timeline rows with created sub-line + status-change badges. Tests: `timeline-drawer.spec.ts` (6 specs incl. "opening a second drawer closes the first group" — N2, "a ShipmentUpdated for the open number re-fetches the timeline", "closing leaves the group and removes the drawer"). |
**Score: 5/5 success criteria verified** (SC1→truth 1; SC2→truths 2+3; SC3→truth 4; SC4→truth 5; SC5→truths 6+7).
### Required Artifacts
| Artifact | Expected | Status | Details |
|---|---|---|---|
| `src/Web/Portal.Web/Controllers/TrackController.cs` | /track + timeline build | ✅ VERIFIED | 4 states + rules 1-6, `JsonException` guard (lines 21-128) |
| `src/Web/Portal.Web/Controllers/ShipmentsController.cs` | create-shipment form + POST | ✅ VERIFIED | antiforgery, 400 + validation, escaped redirect (lines 17-45) |
| `src/Web/Portal.Web/Controllers/HomeController.cs` | landing with search form | ✅ VERIFIED | Index + NoStore Error action |
| `src/Web/Portal.Web/Services/PortalApiClient.cs` | HTTP client for both frozen APIs | ✅ VERIFIED | `CreateShipmentAsync`, `GetTrackingAsync`, `GetTimelineAsync`; 404→null, `ApiRequestException` on failure/timeout |
| `src/Web/Portal.Web/Services/{IPortalApiClient,ApiRequestException,PortalDtos}.cs` | abstractions + DTOs | ✅ VERIFIED | testable interface; typed DTOs mirroring API contracts |
| `src/Web/Portal.Web/Models/{TrackingViewModel,TimelineItem,NewShipmentViewModel}.cs` | view models | ✅ VERIFIED | required-destination validation, `TimelineItem` with optional sub-line/badge |
| `src/Web/Portal.Web/Views/{Home/Index,Track/Index,Shipments/New}.cshtml` + `Shared/_StatusBadge.cshtml` | portal views | ✅ VERIFIED | forms, 4 track states, timeline list, badge partial (7/7 map) |
| `src/Web/Portal.Web/Program.cs` + `appsettings.json` | DI + URLs (D7) | ✅ VERIFIED | `PortalApiClient` singleton from `ShipmentApiUrl`/`TrackingApiUrl`; missing key → explicit `InvalidOperationException` (N5 fix, lines 6-9); appsettings points at :5001/:5002 |
| `src/Web/operator-ui/src/app/shipments/shipments.{ts,html,scss}` | operator table + apply + live patch | ✅ VERIFIED | states, badges, per-row select/Apply/Timeline, inline apply error, live row patch/prepend |
| `src/Web/operator-ui/src/app/shipments/timeline-drawer.{ts,html,scss}` | timeline drawer | ✅ VERIFIED | join/leave group, live re-fetch, spec states, shared timeline rules |
| `src/Web/operator-ui/src/app/core/{statuses,models,shipment-api.service,tracking-api.service,tracking.service}.ts` | core services | ✅ VERIFIED | D6 statuses, typed DTOs, REST + SignalR services (single hub connection) |
| `src/Web/operator-ui/src/environments/environment.ts` | API URLs (D7) | ✅ VERIFIED | `localhost:5001`/`5002` — only URL source in the app |
| `src/Web/operator-ui/src/app/app.{config,routes,html}.ts/html` | standalone app shell | ✅ VERIFIED | `provideHttpClient(withFetch)`, default route → `ShipmentsComponent`, `**` redirect |
| `tests/KargoNet.Portal.Tests/` | portal controller tests | ✅ VERIFIED | `Mvc.Testing` 10.0.15, `FakePortalApiClient`, `PortalFactory` (TestServer, real `Program.cs` startup, fake client) |
| operator-ui `*.spec.ts` (5 spec files) | vitest specs | ✅ VERIFIED | 26 specs across app/statuses/tracking.service/shipments/timeline-drawer |
| `KargoNet.sln` | both projects wired | ✅ VERIFIED | `KargoNet.Portal.Web` + `KargoNet.Portal.Tests` added; `dotnet build KargoNet.sln` 0 errors at HEAD |
### Key Link Verification
| From | To | Via | Status | Details |
|---|---|---|---|---|
| `HomeController` / `Home/Index.cshtml` | `TrackController`, `ShipmentsController` | `asp-controller`/`asp-action` form + link | ✅ WIRED | search form → `Track/Index?number=`; "Create shipment" link → `Shipments/New` |
| `TrackController` | Tracking API | `IPortalApiClient.GetTrackingAsync` / `GetTimelineAsync` | ✅ WIRED | injected via DI (`Program.cs:10-12`); 404/400/5xx handled |
| `ShipmentsController` | Shipment API | `IPortalApiClient.CreateShipmentAsync` | ✅ WIRED | POST → `api/shipments`; `ApiRequestException` → error banner (no 5xx leak) |
| `ShipmentsComponent` | Shipment API | `ShipmentApiService` (list/get/PUT status) | ✅ WIRED | `load()`, `applyStatus()`, `prependRow` id resolution |
| `ShipmentsComponent` / `TimelineDrawerComponent` | SignalR hub | `TrackingService` (`shipmentUpdated`, `connectionState`, `joinGroup`/`leaveGroup`) | ✅ WIRED | single hub connection (D4); drawer join/leave lifecycle |
| `TimelineDrawerComponent` | Tracking API | `TrackingApiService.getTimeline` | ✅ WIRED | `encodeURIComponent` (N4, `tracking-api.service.ts:13`) |
| `statuses.ts` | templates | `STATUS_BADGE_CLASS` / `STATUS_OPTIONS` imports | ✅ WIRED | badges in `shipments.html:45` + `timeline-drawer.html`; no hardcoded status strings in templates |
| `environment.ts` | both API services + hub URL | `environment.shipmentApiUrl` / `trackingApiUrl` | ✅ WIRED | only URL source; matches D7 |
| Portal views | `_StatusBadge` partial | `Html.Partial` in `Track/Index.cshtml` | ✅ WIRED | status card badge renders 7/7 variants |
### Behavioral Spot-Checks
| Check | Command / Evidence | Result | Status |
|---|---|---|---|
| Full solution builds | `~/.dotnet/dotnet build KargoNet.sln` (orchestrator, HEAD `44fc598`) | 0 errors, 0 warnings | ✅ PASS |
| Full solution tests | `~/.dotnet/dotnet test KargoNet.sln` (orchestrator) | **90/90 passed** (12 Portal + 78 Tracking — frozen services regression-free) | ✅ PASS |
| Operator production build | `cd src/Web/operator-ui && npm run build` (orchestrator) | clean (ng build, no errors) | ✅ PASS |
| Operator unit tests | `cd src/Web/operator-ui && npm test -- --watch=false` (orchestrator) | **26/26 passed** | ✅ PASS |
| Named regression — M1 select preservation | vitest "keeps the status select preselected to the row status across a passive CD pass" | pass (in 26/26 run) | ✅ PASS |
| Named regression — N2 sequential drawer | vitest "opening a second drawer closes the first group" | pass (in 26/26 run) | ✅ PASS |
| Named regression — N6 connection indicator | vitest "the indicator reflects the three connection states" | pass (in 26/26 run) | ✅ PASS |
| Timeline mapping edge cases | `Track_WeirdEvent_RendersRawEventTypeLabel` + drawer "renders the eventType text when payloadJson is invalid" | pass (in 90/90 + 26/26 runs) | ✅ PASS |
| Frozen surfaces untouched | `git diff --stat 0bb0dec..HEAD -- src/Services src/Shared` | empty — no service/shared changes in Phase 5 | ✅ PASS |
| Phase 5 file scope | `git diff --name-only 0bb0dec..HEAD` | only `src/Web/**`, `tests/KargoNet.Portal.Tests/**`, `KargoNet.sln`, `.planning/**` | ✅ PASS |
### Anti-Patterns Found
None blocking.
- **Debt markers:** grep for `TBD|FIXME|XXX|TODO|HACK|PLACEHOLDER|not implemented|coming soon` across `src/Web/` + `tests/KargoNet.Portal.Tests/` → **zero matches**.
- **Skipped tests:** none in Portal xunit or operator vitest suites.
- **Review findings closure (1 MAJOR + 7 MINOR, `05-REVIEW.md`):**
  - **M1** (select loses preselected status on CD) — FIXED: `RowMeta` now holds `statusSelect: string | null` with `selectFor()` falling back to `row.status` (`shipments.ts:17-21,96-108`), `[selected]="opt === selectFor(row)"` (`shipments.html:54`), `applyStatus` posts `selectFor(row)` (`shipments.ts:119`); regression spec added.
  - **N1** (unhandled SignalR rejects) — FIXED: `.catch(() => {})` on `start`/`invoke` (`tracking.service.ts:64,69,73`).
  - **N2** (second drawer leaves first group subscription live) — FIXED: `this.sub?.unsubscribe()` before re-subscribe (`timeline-drawer.ts:110`).
  - **N3** (banner copy deviated from spec) — FIXED: spec copy restored verbatim (`shipments.html:19`).
  - **N4** (no path-segment encoding) — FIXED: `encodeURIComponent` in `tracking-api.service.ts:13`, `shipment-api.service.ts:17,23`.
  - **N5** (missing URL key → opaque `ArgumentNullException`) — FIXED: explicit `InvalidOperationException` with key name (`Program.cs:6-9`).
  - **N6** (two independent subscriptions, one unsubscribed) — FIXED: combined `Subscription` (`shipments.ts:60-71`).
  - **N7** (orphan CSS rules) — FIXED: three orphan rules removed; ⚠️ one cosmetic residue: `.alert .close-btn` (`styles.scss:166`) has no matching class in any template (drawer close uses `.drawer-close`, alerts use `.btn-outline-secondary`). Dead selector only — no functional impact; ℹ️ Info, not a gap.
- **No stubs/placeholders:** all controllers, views, services, and components contain full implementations; every state (loading/empty/error/populated) is rendered with spec copy.
### Requirements Coverage
| Req | Description | Status | Evidence |
|---|---|---|---|
| CUST-01 | Create shipment via web form | ✅ SATISFIED | SC1 evidence (form → `POST /api/shipments` → redirect to `/track`) |
| CUST-02 | Search by tracking number | ✅ SATISFIED | SC2 evidence (`/track?number=` → `GET /api/tracking/{n}`) |
| CUST-03 | Status card + full timeline | ✅ SATISFIED | SC2+SC3 evidence (badge + timeline rules 1-6) |
| OPS-01 | Table of all shipments + status badges | ✅ SATISFIED | SC4 evidence (`GET /api/shipments` → table + `STATUS_BADGE_CLASS`) |
| OPS-02 | Per-row status update (7 statuses, Apply) | ✅ SATISFIED | SC5 truth 5 evidence (`PUT /api/shipments/{id}/status` + inline error) |
| OPS-03 | Live updates via SignalR (no refresh) | ✅ SATISFIED | SC5 truth 6 evidence (hub `ShipmentUpdated` → row patch/prepend + indicator) |
| OPS-04 | Timeline drawer with live re-fetch | ✅ SATISFIED | SC5 truth 7 evidence (group join/leave + re-fetch on update) |
### Test Quality Audit
| Suite | Tests | Coverage |
|---|---|---|
| `KargoNet.Portal.Tests` (xunit, `Mvc.Testing` 10.0.15) | 12 (Home 1, Shipments 4, Track 7) | All 4 track states, timeline rules 1-6 (created sub-line, status badge, unknown raw label, unparseable payload guard), form render/validation/redirect/API-failure banner. Real `Program.cs` startup via `TestServer` with `FakePortalApiClient` — no network. |
| operator-ui (vitest + Angular TestBed, `provideHttpClient` with `HttpTestingController`) | 26 (app 2, statuses 2, tracking.service 7, shipments 9, timeline-drawer 6) | All four list states + spec copy, badge rendering, Apply 200/500 (selection kept), live patch/prepend + id resolution, indicator 3 states, hub contract (start/dispose idempotency, join/leave, payload filtering), drawer group lifecycle incl. sequential-drawer (N2), payload edge cases, M1 regression. |
| **Total** | **38 Phase 5 tests** | 0 skipped, 0 failing (orchestrator runs at HEAD: 90/90 solution, 26/26 operator). |
### Human Verification
None required beyond Phase 6. Live browser E2E (visual flows, real hub round-trip in a browser) and live Postgres/Redis/RabbitMQ are documented Phase 6 deferrals (no Docker on this machine) — same treatment as Phase 4. All Phase 5 behavior is exercised by the 38 unit/controller tests above.
### Deferred to Phase 6 (Not Gaps)
| Item | Roadmap basis |
|---|---|
| Live browser end-to-end (portal create → track → operator live update) | Phase 6 SC5: "End-to-end flow verified in the portal and operator UI with live updates" |
| Live Postgres / Redis / RabbitMQ stack (compose) | Phase 6 SC1-SC2: "docker compose up", live API behavior |
| nginx reverse-proxy in front of the three services | Phase 6 out-of-scope: "nginx reverse proxy configuration" |
| README run instructions | Phase 6 SC4: "README updated with the full run instructions" |
### Gaps
None.
### Verification Summary
All five Phase 5 success criteria verified against the code and test evidence: the customer portal (MVC + Razor, antiforgery, 4 states, timeline rules 1-6) and the operator dashboard (Angular 21 standalone, SignalR single-connection, live row patch/prepend, per-row status apply, timeline drawer with group join/leave) are fully implemented and wired to the frozen Phase 2/3/4 APIs. 38 new Phase 5 tests pass; the full solution remains at 90/90 with zero changes to `src/Services` and `src/Shared` (`git diff 0bb0dec..HEAD -- src/Services src/Shared` is empty). All 1 MAJOR + 7 MINOR review findings are fixed in code with regression specs where applicable (M1, N2). One cosmetic dead CSS selector remains (`styles.scss:166`, ℹ️ Info). Live browser E2E and live infrastructure are Phase 6 deferrals, not gaps.
**Status: passed (5/5)**
