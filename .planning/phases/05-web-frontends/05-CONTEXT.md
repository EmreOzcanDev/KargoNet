# Phase 5 Context: Web Frontends

**Goal**: Customers can create and track shipments through the MVC portal; operators can manage shipments with live updates through the Angular screen.

**Requirements**: CUST-01..03 (Portal.Web), OPS-01..04 (operator-ui)

**Depends on**: Phase 4 (Tracking.Api read path + SignalR hub) and Phase 3 (Shipment.Api write path)

## Existing Surfaces (frozen by Phases 3–4)

### Shipment.Api (write path)
- `POST /api/shipments` — body `CreateShipmentRequest { trackingNumber?, description?, destination? }` → `201` `ShipmentDto` (tracking number auto-generated when absent)
- `GET /api/shipments` → `ShipmentDto[]` (all shipments)
- `GET /api/shipments/{trackingNumber}` → `ShipmentDto`
- `PUT /api/shipments/{id}/status` — body `UpdateStatusRequest { status? }` → `ShipmentDto`; status is a string name from the 7-value `ShipmentStatus` enum (Created, PickedUp, InTransit, AtHub, OutForDelivery, Delivered, Exception)
- `ShipmentDto`: `{ id, trackingNumber, description?, destination, status, createdAt, updatedAt }`

### Tracking.Api (read path + realtime)
- `GET /api/tracking/{trackingNumber}` → `TrackingDto { trackingNumber, description, destination, status, createdAt, updatedAt }` (cache-aside; 404 when unknown)
- `GET /api/tracking/{trackingNumber}/events` → `TrackingEvent[]` ordered by `occurredAt`: `{ eventId, trackingNumber, eventType, payloadJson, occurredAt }`; `eventType` is `shipment.created` or `shipment.status.changed`; `payloadJson` is camelCase payload JSON (`shipment.created` → `{ trackingNumber, description, destination }`, `shipment.status.changed` → `{ trackingNumber, status }`)
- SignalR hub at `/hubs/tracking`: server→client method `ShipmentUpdated(TrackingUpdatedDto)` broadcast to `Clients.All` and to the per-number group; client→server `JoinTrackingGroup(trackingNumber)` / `LeaveTrackingGroup(trackingNumber)`
- DLQ admin: `GET /api/admin/dlq` → `{ count }`, `POST /api/admin/dlq/replay` → `{ replayed }`

### Frontend shells (scaffolded Phase 1)
- `src/Web/Portal.Web` — ASP.NET Core MVC (.NET 10), Bootstrap + jQuery bundled, references KargoNet.Contracts
- `src/Web/operator-ui` — Angular 22 SPA (`--ssr false`), standalone components, `app.routes.ts`

## Locked Design

### D1 — Portal.Web is a server-rendered MVC app with a small client layer for create/track
Bootstrap (already bundled) is the UI framework. Three pages:
- `/` (Home): landing with the tracking lookup form (primary action) and a secondary "Create shipment" link.
- `/track` (GET `?number=`): lookup result. No number → empty state prompting to search. Number found → status card (tracking number, description, destination, status badge, timestamps) + full event timeline rendered from `payloadJson` (created → one row; each status change → a status row). Unknown number → "No shipment found for {number}" error state.
- `/shipments/new` (GET form, POST create): form fields tracking number (optional, auto-generated server-side), description, destination. On success redirect to `/track?number={generated-or-given}` and show the result. Validation: required destination; description optional.

### D2 — Portal data access is a minimal typed HTTP client, not raw `HttpClient` scattered in views
One `PortalApiClient` (injected singleton) with `GetTrackingAsync(number)`, `GetTimelineAsync(number)`, `CreateShipmentAsync(req)`, talking to `ShipmentApiUrl` (create) and `TrackingApiUrl` (track/timeline) from configuration. Controllers stay thin; views render view models. 404s map to "not found" view state, not exceptions.

### D3 — Operator screen is a two-route SPA: shipments list (default) + no separate detail route
- `/` (default, `ShipmentsComponent`): table of all shipments (columns: tracking number, description, destination, status badge, updated) sourced from `GET /api/shipments`; a status-update control per row (select of the 7 statuses + Apply, posting `PUT /api/shipments/{id}/status`); a "Timeline" action per row opening a detail drawer (D5); a live-updates indicator (connected / reconnecting).
- The detail drawer is in-page (component-level), not a route — keeps hub subscription state stable while the drawer is open.

### D4 — SignalR lifecycle: one persistent connection for the app, not per-row
A `TrackingService` (injected in `ShipmentsComponent`) owns one `SignalR` connection to `{TrackingApiUrl}/hubs/tracking` created on component init. It subscribes to `ShipmentUpdated`, patches the in-memory table row for that tracking number (the hub broadcast beats a table refetch; a row is appended when a new tracking number arrives), and tracks connection state (`connected` / `reconnecting` / `disconnected`) for the indicator. Default `HubConnectionBuilder` auto-reconnect policy. `JoinTrackingGroup`/`LeaveTrackingGroup` are used only by the timeline drawer for its live refresh (D5).

### D5 — Timeline drawer fetches the timeline and live-refreshes on `ShipmentUpdated`
`TimelineDrawer` component: on open it calls `GET {TrackingApiUrl}/api/tracking/{n}/events` and renders the same timeline as the portal (created row + one row per status change, parsed from `payloadJson`); it joins the tracking group on open and leaves it on close. While open, each `ShipmentUpdated` for that number re-fetches the timeline so the drawer is live without a manual refresh.

### D6 — Status lifecycle constants are a shared TypeScript const, never hardcoded in templates
The 7 status names (Created, PickedUp, InTransit, AtHub, OutForDelivery, Delivered, Exception) live in one `statuses.ts` module in operator-ui (mirroring the Contracts enum) and drive both the update select and the badge color map. The portal renders status server-side from the string; no enum there.

### D7 — Both apps read API base URLs from environment/config, never hardcoded ports
- Portal.Web: `ShipmentApiUrl` + `TrackingApiUrl` from `appsettings.json` (dev defaults `http://localhost:5001` / `http://localhost:5002`).
- operator-ui: `environment.ts` (`shipmentApiUrl`, `trackingApiUrl`, same dev defaults); the Phase 6 nginx proxy is the production path — app code stays proxy-agnostic (absolute URLs from env).

### D8 — Error/empty states are explicit, not silent
Every page: loading state (table/list), empty state (no shipments / no timeline events), not-found state (unknown tracking number), and a dismissible error banner for failed API calls (5xx/timeout). No white screens, no unhandled promise rejections.

## Requirement Mapping

| Req | Delivered by |
|-----|--------------|
| CUST-01 create shipment (number, description, destination) | `/shipments/new` form → `POST /api/shipments` (D1, D2) |
| CUST-02 look up by tracking number | `/track?number=` → `GET /api/tracking/{n}` (D1, D2) |
| CUST-03 current status + full event timeline | `/track` status card + timeline from `/api/tracking/{n}/events` (D1) |
| OPS-01 shipments table with status badges | `ShipmentsComponent` table from `GET /api/shipments` + badge map (D3, D6) |
| OPS-02 update status through lifecycle | per-row select + `PUT /api/shipments/{id}/status` (D3, D6) |
| OPS-03 live updates via SignalR, no refresh | `TrackingService` persistent hub connection, `ShipmentUpdated` patches table (D4) |
| OPS-04 event timeline in detail drawer | `TimelineDrawer` + group join/leave + live re-fetch (D5) |

## Out of Scope (Phase 6)
- Dockerfiles, docker-compose.yml, nginx proxy config, .env wiring
- Live end-to-end runs against real Postgres/Redis/RabbitMQ
- README demo steps
