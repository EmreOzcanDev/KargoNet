# Phase 5 Code Review

**Scope:** diff `0bb0dec..184d340` — `src/Web/Portal.Web/**`, `src/Web/operator-ui/**`, `tests/KargoNet.Portal.Tests/**`.
**Reviewer:** gsd-code-reviewer (2026-11-20).
**Verdict:** 1 MAJOR, 7 MINOR, 0 BLOCKER.

## Verified clean

- **Frozen surfaces**: diff touches zero files under `src/Services/` or `src/Shared/`.
- **Security**: no `innerHTML`/`dangerouslySet*`; no secrets in operator-ui; `Uri.EscapeDataString` on portal URL building; Razor escaping; Angular `{{ }}` interpolation only.
- **Badge map**: 7/7 in `statuses.ts`, `_StatusBadge.cshtml`, `styles.scss` — hex values match the UI-SPEC map exactly.
- **Copy fidelity**: portal pages exact; operator O1 empty/loading/apply-error/indicator copy exact.
- **SignalR single connection**: `start()`/`dispose()` idempotent, `ngOnDestroy`-safe; state mapping `Live`/`Reconnecting…`/`Offline`.
- **Timeline parsing**: both apps match shared rules 1–6 (ascending sort, created/status-changed mapping, unknown-type and unparseable-payload fallbacks).
- **Portal redirect flow**: 302 → `/track?number={escaped}`; 400 re-render with preserved values; API failure → dismissible banner; antiforgery on POST.
- **Tests**: portal xunit covers all four track states, malformed `payloadJson`, antiforgery + redirect follow, 400 validation; vitest specs assert badge classes, states, row patch/append, id resolution, group join/leave, connection states, with `HttpTestingController.verify()`.

## Findings

### MAJOR

**M1 — Status select preselect degrades to the first option after the first change-detection pass; `Apply` on a pristine row posts the tracking number as the status**
`src/Web/operator-ui/src/app/shipments/shipments.ts:96,102-105`
`meta()` initializes `statusSelect: trackingNumber`, and `selectFor(row)` returns `m.statusSelect` whenever meta exists. The template's `[value]="selectFor(row)"` is only correct on the first table render pass (before `meta(row.trackingNumber)` is created by the `[disabled]` binding). On every subsequent CD pass — which happens seconds after load when the hub emits `connected` — the select value becomes the tracking number (no matching option → browser shows the first option, `Created`). UI-SPEC "current status preselected" is violated, and `applyStatus` (line 116) sends the tracking number as `status` → 400 → `Couldn't update status` for any row the user never touched. The specs miss this because they only assert select values *after* a `change` event.
**Fix:**
```ts
interface RowMeta { statusSelect: string | null; applyError: boolean; selectDisabled: boolean; }
// meta(): m = { statusSelect: null, applyError: false, selectDisabled: false };
// selectFor(row): return this.rowMeta.get(row.trackingNumber)?.statusSelect ?? row.status;
```
Add a spec asserting `select.value === row.status` after a passive CD pass (e.g. after `fake.connectionState.next('connected')`).

### MINOR

**N1 — Group join/leave race + unhandled rejections** — `src/Web/operator-ui/src/app/core/tracking.service.ts:43-74`
`joinGroup` calls `start()` then immediately `void this.connection?.invoke('JoinTrackingGroup', n)`; if the connection is still `Connecting`/`Reconnecting`, `invoke` rejects and the group join is silently lost (same for `leaveGroup`; `void connection.start()` rejects unhandled when the hub is down). D8 requires "no unhandled promise rejections."
**Fix:** chain `.catch(() => {})` on the invokes and on `start()`; and/or defer invokes until `stateChanged` reports Connected.

**N2 — Drawer subscription leak on sequential opens** — `src/Web/operator-ui/src/app/timeline-drawer/timeline-drawer.ts:101-117`
`set trackingNumber` overwrites `this.sub` without unsubscribing the previous one. Each open-while-open leaves a zombie subscription whose closure re-triggers `load()` when an update for the *old* number arrives → spurious extra timeline fetch + loading flicker for the new drawer.
**Fix:** `this.sub?.unsubscribe();` at the top of the setter's re-subscribe path.

**N3 — UI-SPEC copy deviation (O1 error banner)** — `src/Web/operator-ui/src/app/shipments/shipments.html:19`
Renders `…Check that the Shipment API is running and try again.`; spec copy is `We couldn't reach the KargoNet service. Check your connection and try again.` (portal uses the spec copy).
**Fix:** restore the spec sentence.

**N4 — Unencoded path segments in operator API URLs** — `src/Web/operator-ui/src/app/core/tracking-api.service.ts:11-15`, `src/Web/operator-ui/src/app/core/shipment-api.service.ts:15-19,21-26`
`trackingNumber` (free customer input) is interpolated into the URL without `encodeURIComponent`; a number containing `/` or `?` malforms the request.
**Fix:** `encodeURIComponent(trackingNumber)` in both services.

**N5 — `Program.cs:8-10` null-forgiving on configuration** — `src/Web/Portal.Web/Program.cs`
`new Uri(builder.Configuration["ShipmentApiUrl"]!)` throws a cryptic `ArgumentNullException` at startup if a key is missing.
**Fix:** fail with an explicit message, e.g. `?? throw new InvalidOperationException(...)`.

**N6 — Unguarded `connectionState` subscription** — `src/Web/operator-ui/src/app/shipments/shipments.ts:51-69`
The `connectionState.subscribe(...)` in `ngOnInit` is not stored/unsubscribed (only the `shipmentUpdated` sub is).
**Fix:** add it to the combined `Subscription`.

**N7 — Dead CSS** — `src/Web/operator-ui/src/styles.scss:246,285,288,290-298`
`select.row-select`, `.drawer-header .drawer-number`, `.drawer-header .close-btn` match nothing.
**Fix:** delete the orphan rules.

## Fixes

Applied 2026-11-20 by gsd-code-fixer. All gates green: `dotnet build KargoNet.sln` 0 errors; `dotnet test KargoNet.sln` 12 + 78 passed; operator-ui `npm run build` clean and `npm test -- --watch=false` 26 passed.

- **M1** — `e5e4f4f` (`RowMeta.statusSelect: string | null`, `meta()` null init, `selectFor` falls back to `row.status`, regression spec), `1c28aa9` (`applyStatus` posts `selectFor(row)`), `09546c7` (template preselects via `[selected]` on the matching `<option>` so the selection is set and persists across passive CD passes).
- **N1** — `07e6f1d` (guard `start()`, `JoinTrackingGroup`, `LeaveTrackingGroup` against unhandled rejections).
- **N2** — `07a780c` (`this.sub?.unsubscribe()` before re-subscribing in `setTrackingNumber`).
- **N3** — `988c33d` (restore UI-SPEC O1 banner copy verbatim), `bce083e` (align the banner spec assertion with the spec copy).
- **N4** — `2046a91` (`encodeURIComponent` on tracking numbers in `getTimeline`, `getShipment`, and `updateStatus`).
- **N5** — `0d5b333` (fail explicitly with `InvalidOperationException` on missing Portal API config keys).
- **N6** — `86c7f89` (combine the `shipmentUpdated` and `connectionState` subscriptions into one `Subscription` in `ngOnInit`).
- **N7** — `fa9db0e` (remove orphan `select.row-select`, `.drawer-header .drawer-number`, `.drawer-header .close-btn` CSS rules).

Frozen surfaces (`src/Services/*`, `src/Shared/*`) untouched.
