# Phase 5 Discussion Log — Web Frontends

Autonomous run (no human gates). Questions raised and resolved with safe defaults:

| # | Question | Resolution |
|---|----------|------------|
| Q1 | Should the portal track view be server-rendered, client-rendered, or hybrid? | Server-rendered MVC (D1): the portal is a read-heavy customer surface; Bootstrap is already bundled; no SPA complexity needed. Views render view models from `PortalApiClient` (D2). |
| Q2 | Which API does the portal create form post to — Shipment.Api directly? | Yes (D1/D2): `POST /api/shipments` is the only create path; the portal gets the tracking number back and redirects to `/track?number=…`. No new endpoints are added to either API in Phase 5. |
| Q3 | Should the timeline endpoint return structured rows instead of raw `payloadJson`? | No — the endpoint is frozen (Phase 4). The portal and the drawer parse `payloadJson` (D1, D5); it is stable camelCase JSON with two known shapes (`shipment.created`, `shipment.status.changed`). |
| Q4 | One SignalR connection per row, one per shipment page, or one per app? | One per app lifetime of the shipments screen (D4): a single `TrackingService` owns the connection and patches rows in place; group join/leave is scoped to the drawer (D5). |
| Q5 | Should the operator detail be a separate route? | No (D3): an in-page drawer keeps the hub subscription and table state alive while the operator inspects a shipment; a route would destroy/recreate the connection state on back-navigation. |
| Q6 | How do the apps reach the APIs in dev vs. Docker? | Config/env only (D7): `appsettings.json` keys for the portal, `environment.ts` for the SPA. Phase 6's nginx proxy is configured around these URLs; no code change needed for the proxy. |
| Q7 | Should the portal get live updates too (customer sees status change without refresh)? | No — CUST-03 requires current status + timeline on lookup, which the read endpoints satisfy; live customer updates are not in the CUST/OPS requirement set. The `/track` page is re-fetchable via the normal browser flow. |
| Q8 | Status select: free text or fixed dropdown? | Fixed dropdown of the 7 enum names from one shared `statuses.ts` (D6); the API also validates server-side, so a stale dropdown fails safely. |
| Q9 | Do frontends need auth? | No — project constraint (no auth anywhere); the screens are open per PLAN.md. |
| Q10 | What test strategy applies to frontends? | Build gates (`dotnet build` for Portal.Web, `ng build` for operator-ui) plus `MvcTesting`-level endpoint smoke tests where practical; live browser E2E is Phase 6 (no Docker yet). No new xunit test project in this phase — the existing test suite stays the regression net for the APIs the UIs call. |

All resolutions recorded as D1–D8 in `05-CONTEXT.md`.
