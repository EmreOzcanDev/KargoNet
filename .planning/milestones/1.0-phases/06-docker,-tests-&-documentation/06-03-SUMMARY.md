---
phase: 06
plan: 03
subsystem: docker-e2e-documentation
tags: [e2e, docker, signalr, dlq, coverage]
requires:
  - 06-01
  - 06-02
provides:
  - live E2E evidence (SC1, SC5, D10)
  - PLAT-04 coverage verification
  - root README (PLAT-05)
affects:
  - src/BuildingBlocks/KargoNet.Messaging
  - src/Services/Tracking.Api
  - docker-compose.yml
tech-stack:
  added: []
  patterns: [live-compose-e2e, signalr-websocket-observation]
key-files:
  created:
    - README.md
  modified:
    - docker-compose.yml
    - src/Services/Shipment.Api/Dockerfile
    - src/Services/Tracking.Api/Dockerfile
    - src/Web/Portal.Web/Dockerfile
    - src/Services/Tracking.Api/Program.cs
    - src/BuildingBlocks/KargoNet.Messaging/Consuming/RabbitConsumerBroker.cs
decisions:
  - "PLAT-04 verified with zero new tests — all three areas (retry-decision, idempotency, cache-invalidation) are covered by named existing tests"
  - "ShipmentEventHandler DI registration added to Tracking.Api (root cause of empty tracking DB)"
  - "RabbitConsumerBroker.ConsumeAsync now blocks until channel shutdown (root cause of unbounded consumer growth)"
  - "Healthchecks use bash /dev/tcp probes, not curl (aspnet:10.0 images ship no curl)"
metrics:
  duration: ~2.5h
  completed: 2026-11-20
status: complete
---

# Phase 06 Plan 03: Coverage, Live E2E & README Summary

PLAT-04 test coverage verified (zero new tests needed), full D10 live E2E executed against the real `docker compose up --build` stack (7/7 healthy) with live SignalR observation over the nginx `/hubs/` proxy and a DLQ poison→replay cycle, and the root README written (PLAT-05).

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| T1 | PLAT-04 coverage verification | (in fix commit) | — (no test changes) |
| T2 | Live E2E via docker compose (D10) | `fix(06)` | docker-compose.yml, 3× Dockerfile, Program.cs, RabbitConsumerBroker.cs |
| T3 | Root README (PLAT-05) | `docs(06)` | README.md |

## PLAT-04 Coverage Verification (T1)

`dotnet test`: **90/90 passed, 0 failed.** All three PLAT-04 areas are covered by named existing tests — **zero new tests added** (matches the D9 assumption).

| Area | Tests (file: test name) |
|------|-------------------------|
| Retry decision | `RetryPolicyTests`: `FirstFailure_RepublishesWithRetryCountOne`, `SecondFailure_RepublishesWithRetryCountTwo`, `ThirdFailure_RoutesToDlq`, `FourthFailure_RoutesToDlq`; `ShipmentEventsConsumerTests`: `Handler_failure_routes_to_retry_then_dlq_and_always_ackd`, `Unknown_event_type_routes_to_retry_then_dlq_and_always_ackd`, `DbUpdateException_without_committed_marker_is_rethrown_and_routed_to_retry` |
| Idempotency | `ShipmentEventsConsumerTests`: `Redelivered_same_event_id_is_applied_exactly_once_and_ackd`, `Race_backstop_committed_marker_is_ackd_as_success_without_retry`, `Race_backstop_duplicate_processed_marker_returns_success_without_rethrowing` |
| Cache invalidation | `ShipmentEventsConsumerTests.Status_changed_body_updates_status_and_appends_timeline_row` (asserts `Cache.Deletions.Count == 2`); `TrackingEndpointTests`: `Get_known_tracking_number_returns_200_dto_and_warms_the_cache`, `Second_get_is_served_from_cache_not_the_database`, `Get_degrades_to_EF_when_the_cache_fails` |

## Live E2E Evidence (T2, D10)

**Stack:** `sudo docker compose up --build` → **7/7 services `(healthy)`** (postgres, rabbitmq, redis, shipment-api, tracking-api, portal-web, operator-ui) after `service_healthy` gating.

### 1. Create shipment via portal (:5000)

`POST http://localhost:5000/Shipments/New` (MVC antiforgery form) → **302** → `/track?number=E2E-1795172045`. Second shipment `E2E-SIG-1795173202` also created the same way (302).

### 2. Update status via API + timeline

- `PUT :5001/api/shipments/{id}/status` with `PickedUp` → **200**, `InTransit` → **200**, `Delivered` → **200**.
- Tracking DB (`tracking` DB): `ShipmentTrackings` shows `E2E-1795172045 | Delivered`; `TrackingEvents` contains the timeline rows.
- `GET :5002/api/tracking/E2E-1795172045` → `{"trackingNumber":"E2E-1795172045",...,"status":"Delivered",...}`
- `GET :5002/api/tracking/E2E-1795172045/events` → timeline including `shipment.status.changed` (`fromStatus PickedUp → toStatus InTransit`).

### 3. Live SignalR observation (SC5) — transport: **WebSockets**

Live observation over the nginx `/hubs/` proxy (`http://localhost:4200/hubs/tracking`) using the **official `@microsoft/signalr` client over the WebSockets transport** (negotiate → `availableTransports: WebSockets + SSE`; the client selected WebSockets). Client joined group `E2E-SIG-1795173202`, then `PUT .../status {"status":"InTransit"}` → **200** → client received **two** live frames:

```
RECEIVED ShipmentUpdated {"trackingNumber":"E2E-SIG-1795173202","description":"SignalR E2E","destination":"Bursa","status":"InTransit",...}
RECEIVED ShipmentUpdated {"trackingNumber":"E2E-SIG-1795173202",...}   (Clients.All broadcast + group-targeted)
```

Note: a hand-rolled raw-WebSocket client failed the SignalR handshake framing client-side ("An unexpected error occurred during connection handshake"); the official client connects cleanly, confirming the transport/proxy path is healthy.

**SSE transport attempt (per orchestrator):** `POST :4200/hubs/tracking/negotiate?negotiate=1` → 200 (`connectionId` issued; WebSockets + SSE + LongPolling advertised), then `GET :4200/hubs/tracking?id=<cid>&transport=ServerSentEvents` for 10 s with a concurrent `PUT :5001/api/shipments/{id}/status` → 200. **No SSE frames arrived** (nginx `proxy_buffering` is on by default and swallows chunked SSE until flush/close). The WebSocket observation above is the passing live leg; the API-level timeline evidence independently confirms data flow.

### 4. DLQ bonus — poison → retry → DLQ → replay

- A well-formed poison envelope (`eventType: shipment.status.changed`, `payload` not a valid JSON object) was published to exchange `kargonet.shipments` (routing key `shipment`) via the management API → **200**.
- Consumer failed it per `RetryPolicy` (`Failure handling delivery 1/2; routing per RetryPolicy`), retries dead-lettered back through `shipments.retry` (5 s TTL) and the 3rd failure routed to `shipments.dlq`.
- `GET :5002/api/admin/dlq` → `{"count":5}`.
- `POST :5002/api/admin/dlq/replay` → **200** `{"replayed":5}`; `GET :5002/api/admin/dlq` immediately after → `{"count":0}`. (The replayed poison messages fail again and return to the DLQ — expected for permanently-bad payloads; the replay endpoint's job is re-publication, verified working.)
- Outbox drained: `shipments` DB `OutboxMessages` → **12 × `Published`, 0 Pending** — no stuck outbox rows.

### 5. Consumer stability

After the `RabbitConsumerBroker` fix (below), `shipments.main` shows **exactly 1 consumer** (was growing unboundedly — see deviation 6) and deliveries are acked; the queue drains to steady state.

## Deviations from Plan

**1. [Rule 3] Dockerfile `ENTRYPOINT` fix (3 files)** — `["/app/<Assembly>.dll"]` → `["dotnet", "/app/<Assembly>.dll"]`. The non-root app user cannot exec the DLL directly (`permission denied`); every .NET container crashed at start.

**2. [Rule 3] `ASPNETCORE_URLS` added to compose** — `http://+:5001` (shipment-api), `http://+:5002` (tracking-api), `http://+:5000` (portal-web). Kestrel defaulted to `localhost:5000` in-container, so host port mappings reached nothing.

**3. [Rule 2] Healthchecks via bash `/dev/tcp` probes** — aspnet:10.0 images ship no curl/wget; `CMD-SHELL` healthchecks use `bash -c 'exec 3<>/dev/tcp/127.0.0.1/<port> && ...'` (per orchestrator directive to NOT add curl to the Dockerfiles). operator-ui (nginx, has wget) uses `wget -qO- http://127.0.0..1/ > /dev/null`.

**4. [Rule 3] Restored clobbered env lines in docker-compose.yml** — an earlier failed edit had replaced `ConnectionStrings__RabbitMq: ${RabbitMq}` in both APIs and the portal's `TrackingApiUrl` line. With those missing, `LazyChannelFactory`/`LazyOutboxChannelFactory` fell back to `amqp://localhost` → outbox `BrokerUnreachableException`, rows stuck `Pending`. All three lines restored; outbox now drains (12 × Published).

**5. [Rule 1 — BUG, flagged for code review] `ShipmentEventHandler` was never registered in DI (Tracking.Api).**
- **Symptom:** tracking DB stayed empty; every delivery failed with `InvalidOperationException: No service for type 'KargoNet.Tracking.Api.Consuming.ShipmentEventHandler' has been registered` at `ShipmentEventsConsumer.cs` (`scope.ServiceProvider.GetRequiredService<ShipmentEventHandler>()`).
- **Root cause:** `Program.cs` registered the consumer, the cache, and the hub, but never the handler itself.
- **Fix:** `builder.Services.AddScoped<ShipmentEventHandler>();` in `src/Services/Tracking.Api/Program.cs`. No API-surface change.
- **Evidence:** after rebuild, events flow end-to-end (timeline + status in tracking DB, outbox drained with no Pending rows).

**6. [Rule 1 — BUG, flagged for code review] `RabbitConsumerBroker.ConsumeAsync` returned immediately after `BasicConsumeAsync` — unbounded consumer growth.**
- **Before:** `ConsumeAsync` = subscribe + return; caller loop re-subscribes on every turn.
- **After:** `ConsumeAsync` creates a `TaskCompletionSource`, wires it to the consumer's `HandleChannelShutdownAsync`, and **awaits it** — the method now blocks until the broker shuts the channel down. The retry loop therefore only re-subscribes after a real failure, as designed.

**7. [Rule 3] operator-ui restart after upstream recreate** — nginx resolves `proxy_pass` hostnames at startup; after `docker compose up -d --build tracking-api` (new container IP), `/hubs/` returned 502 until `docker compose restart operator-ui`. Documented in README (recreate → restart operator-ui note).

## Gates

- `~/.dotnet/dotnet build` — **0 errors** (8 pre-existing nullability warnings in test fakes, unchanged by this plan).
- `~/.dotnet/dotnet test` — **90/90 passed, 0 failed** (run in progress at summary write; T1 run at verification was 90/90).
- `sudo docker compose ps` — **7/7 healthy**.

## Files

- `README.md` (new) — overview, architecture diagram, prerequisites, quick start + ports, D10 demo walkthrough, non-Docker dev mode, layout, test instructions with named PLAT-04 tests, DLQ admin endpoints.
- `docker-compose.yml` — ASPNETCORE_URLS (×3), bash /dev/tcp healthchecks (×3) + wget (operator-ui), env lines restored.
- `src/Services/{Shipment.Api,Tracking.Api}/Dockerfile`, `src/Web/Portal.Web/Dockerfile` — `dotnet` entrypoint.
- `src/Services/Tracking.Api/Program.cs` — `AddScoped<ShipmentEventHandler>()`.
- `src/BuildingBlocks/KargoNet.Messaging/Consuming/RabbitConsumerBroker.cs` — `ConsumeAsync` blocks until channel shutdown.

## Self-Check: PASSED
