---
phase: 06-docker,-tests-&-documentation
reviewed: 2026-11-20T00:00:00Z
depth: deep
files_reviewed: 17
files_reviewed_list:
  - .dockerignore
  - .env
  - .gitignore
  - README.md
  - docker-compose.yml
  - docker/postgres/init.sql
  - src/BuildingBlocks/KargoNet.Messaging/Consuming/RabbitConsumerBroker.cs
  - src/Services/Shipment.Api/Dockerfile
  - src/Services/Shipment.Api/Program.cs
  - src/Services/Tracking.Api/Dockerfile
  - src/Services/Tracking.Api/Program.cs
  - src/Web/Portal.Web/Dockerfile
  - src/Web/operator-ui/Dockerfile
  - src/Web/operator-ui/angular.json
  - src/Web/operator-ui/nginx.conf
  - src/Web/operator-ui/src/environments/environment.docker.ts
  - tests/KargoNet.Tracking.Tests/Messaging/RabbitConsumerBrokerTests.cs
findings:
  critical: 1
  major: 3
  minor: 5
  total: 9
status: issues_found
verdict: PASS WITH FINDINGS
---

# Phase 6: Code Review Report

**Reviewed:** 2026-11-20
**Depth:** deep (per-file reads + cross-file tracing: consumer retry loop ↔ broker TCS ↔ channel factory; operator-ui services ↔ nginx proxy ↔ API route maps; README claims ↔ appsettings/package.json)
**Files Reviewed:** 17
**Status:** issues_found
**Verdict:** **PASS WITH FINDINGS** — 1 Critical (broken operator-screen feature in the Docker stack), 3 Major, 5 Minor. None block the core reliability story (outbox → retry → DLQ, idempotency, cache) which was verified intact; CR-01 should be fixed before this phase is called done.

## Summary

The changeset implements the locked decisions D1–D11. Verified correct:

- **Broker blocking-consume** (`RabbitConsumerBroker.ConsumeAsync`): the `TaskCompletionSource` completes on the channel-shutdown callback; `LazyChannelFactory.GetChannelAsync` transparently replaces a shut-down channel, so the consumer's retry loop re-subscribes exactly once per real failure — the unbounded-consumer-leak rationale in the comments holds. The two exit paths that do NOT complete the TCS are covered below (MA-01, MI-01).
- **DI registration**: `AddScoped<ShipmentEventHandler>` in `Tracking.Api/Program.cs:34` is correct — the handler's four ctor deps (`TrackingDbContext`, `ITrackingCache`, `IShippingUpdateBroadcaster`, `ILogger`) are all scoped, and `ShipmentEventsConsumer.HandleDeliveryAsync:102-103` resolves it from a fresh scope per delivery.
- **Migrate retry** (both `Program.cs`): 10×3 s bounded loop, `useInMemory` bypass, scope-per-attempt — sound.
- **Compose healthchecks**: bash `/dev/tcp` probes are correct for the curl-less aspnet image (YAML escaping of `\r\n` verified); the live 8/8-healthy stack confirms them.
- **Dockerfiles**: multi-stage, `dotnet <dll>` ENTRYPOINT, `USER app` — correct.
- **Tests**: `RabbitConsumerBrokerTests` updated consistently; `FakeChannel.SimulateChannelShutdown` fires the same callback the real client uses; no test depends on the old return-immediately behavior.
- **`.gitignore` / `.dockerignore` / `.env` / `init.sql` / `angular.json` / `environment.docker.ts`**: consistent with D3/D5/D6.

Findings:

## Critical Issues

### CR-01: nginx `/api/` proxies ALL API traffic to shipment-api — operator-screen timeline 404s in Docker

**File:** `src/Web/operator-ui/nginx.conf:9-15`
**Issue:** The operator UI issues two distinct API families, both under `/api/`:
- `shipment-api.service.ts` → `/api/shipments*` → **shipment-api** (5001)
- `tracking-api.service.ts:13` → `/api/tracking/{number}/events` → **tracking-api** (5002)

`nginx.conf` has a single `location /api/` → `http://shipment-api:5001`, so in the Docker environment (`environment.docker.ts`, `trackingApiUrl: ''`) the timeline drawer's `GET /api/tracking/{n}/events` (used by `timeline-drawer.ts:142`) lands on shipment-api, which has no such route → **404**. The timeline drawer is dead in the composed stack. (The portal is unaffected — it calls tracking-api directly server-side via `PortalApiClient.cs:47`.) This also contradicts D3's stated split ("`/api/` → shipment-api") — the decision itself missed the `/api/tracking/` family.
**Fix:** Add a more-specific location (nginx prefers the longest prefix):
```nginx
location /api/tracking/ {
    proxy_pass http://tracking-api:5002;
    proxy_http_version 1.1;
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
}

location /api/ {
    proxy_pass http://shipment-api:5001;
    ...
}
```

**Status:** resolved — commit `30c551a` (live-verified: `/api/tracking/{n}` and `/api/tracking/{n}/events` reach tracking-api through the composed operator-ui proxy).

## Major

### MA-01: `ConsumeAsync` blocks on the TCS without linking `ct` — graceful shutdown hangs, container gets SIGKILLed

**File:** `src/BuildingBlocks/KargoNet.Messaging/Consuming/RabbitConsumerBroker.cs:106`
**Issue:** `await channelShutdown.Task` has no cancellation linkage. If the host's shutdown token is cancelled while the consume is registered and the channel is still healthy (the normal `docker compose stop` path), the TCS never completes: `ConsumeAsync` never returns, `ShipmentEventsConsumer.ExecuteAsync` never exits, and `BackgroundService.StopAsync` stalls the host shutdown (until the 30 s `ShutdownTimeout` default). Docker's 10 s stop grace then SIGKILLs the container, so every stop of tracking-api is a forced kill — unacked in-flight deliveries are redelivered (harmless at-least-once, but shutdown is never graceful). This is one of the two exit paths that do not complete the TCS (the other: MI-01).
**Fix:**
```csharp
using var cancellation = ct.Register(() => channelShutdown.TrySetCanceled());
// ... then existing:
await channelShutdown.Task.ConfigureAwait(false);
```
On shutdown the await throws `TaskCanceledException` (an `OperationCanceledException`), which `ShipmentEventsConsumer.ExecuteAsync:72` already catches with the correct `when` guard and breaks the loop.

**Status:** resolved — commit `2943056` (caller-token registration added; `RabbitConsumerBrokerTests` covers the cancel path).

### MA-02: `/hubs/` proxy lacks `proxy_buffering off` — SignalR SSE fallback is buffered

**File:** `src/Web/operator-ui/nginx.conf:17-26`
**Issue:** The SignalR JS client uses WebSockets but automatically falls back to Server-Sent Events when the upgrade fails. nginx's default `proxy_buffering on` swallows the chunked SSE stream until buffer thresholds are hit, so any client that falls back to SSE receives `ShipmentUpdated` broadcasts with unbounded (effectively indefinite) delay. The WebSocket headers are present, so the happy path works — this is the degraded path.
**Fix:** Add to `location /hubs/`:
```nginx
proxy_buffering off;
```
(Optionally also `proxy_cache off;` for explicitness, and use the standard `map $http_upgrade $connection_upgrade` recipe for the `Connection` header, though the static `"upgrade"` value is benign for the ASP.NET Core upstream.)

**Status:** resolved — commit `30c551a`.

### MA-03: README dev-mode instructions are false — `dotnet run` cannot find any connection string

**File:** `README.md:80` ("The credentials in the root `.env` are the defaults used by the app configs")
**Issue:** Neither API's `appsettings.json`/`appsettings.Development.json` contains any `ConnectionStrings`, and ASP.NET Core does not load the root `.env` file outside of Docker Compose. A developer following the README's non-Docker section (`dotnet run` in `src/Services/Shipment.Api` / `Tracking.Api`) starts with `useInMemory == false` and `GetConnectionString("ShipmentDb") == null` → the in-process `MigrateAsync` added in this phase throws on first scope, crashing startup. The documented dev flow does not work as written.
**Fix:** Either document the required env vars (`ConnectionStrings__ShipmentDb`, `ConnectionStrings__TrackingDb`, `ConnectionStrings__Redis`, `ConnectionStrings__RabbitMq`, e.g. via `launchSettings.json` or `export`) with localhost hosts, or add matching localhost connection strings to the `appsettings.Development.json` files so `dotnet run` works out of the box.

**Status:** resolved — commit `3ddaaff` (README now states Docker Compose is the supported dev flow and lists the four exact env vars for host-run dev).

## Minor

### MI-01: Broker-side consumer cancel does not complete the TCS

**File:** `src/BuildingBlocks/KargoNet.Messaging/Consuming/RabbitConsumerBroker.cs:169-170`
**Issue:** `HandleBasicCancelAsync` returns `Task.CompletedTask` without signalling `ChannelShutdown`. If the broker cancels the consumer without closing the channel, `ConsumeAsync` blocks forever with no deliveries and the retry loop never re-subscribes. Rare for a non-exclusive consumer (queue deletion closes the channel, which does fire the shutdown callback), hence Minor.
**Fix:** Complete the TCS in `HandleBasicCancelAsync` as well (a cancel means the consumer is dead; the retry loop will re-subscribe).

**Status:** open — not in the assigned fix scope; left for a future pass.

### MI-02: MigrateAsync retry loop swallows the first 9 failures silently

**File:** `src/Services/Shipment.Api/Program.cs:41-44`, `src/Services/Tracking.Api/Program.cs:54-57`
**Issue:** `catch (Exception) when (attempt < 10)` discards the exception with no logging; a misconfigured connection string produces 9 silent 3 s delays and then a raw unhandled exception. The final-attempt crash is acceptable (container goes unhealthy), but the silent retries make a misconfiguration hard to diagnose from container logs.
**Fix:** Log each attempt, e.g. `Console.Error.WriteLine($"[migrations] attempt {attempt} failed: {ex.Message}")`, or build a logger from `builder` before the loop.

**Status:** resolved — commit `cf98131` (both `Program.cs` now log `Warning [migrations] attempt {n} of 10 failed: ...`).

### MI-03: README test instructions mislabel the JS suite

**File:** `README.md:105` (`npm test # vitest suite`)
**Issue:** `package.json:9` defines `"test": "ng test"` — the Angular builder (Karma), not vitest.
**Fix:** Change the comment to `# Angular test suite (ng test)`.

**Status:** resolved — commit `3ddaaff`. (Note: the reviewer's "not vitest" premise was wrong — `ng test` here is the vitest-backed Angular builder (`vitest ^5.0.0` in devDependencies); README now says "ng test, vitest-backed".)

### MI-04: `.env` ships real-looking credentials (approved by D6, noted for hygiene)

**File:** `.env:1-8`
**Issue:** `kargonet/kargonet` credentials and connection strings are git-tracked. D6 explicitly approved this for a local-only default stack, and `.dockerignore` keeps `.env` out of build contexts — so this is a confirmation note, not a defect. If this repo is ever shared or made public, these must be rotated/overridden.
**Fix:** None required for this phase; the README already says "change them for anything shared."

**Status:** not required — D6 approved; no action.

### MI-05: Compose services have no `restart:` policy

**File:** `docker-compose.yml` (all 8 services)
**Issue:** After a container crash (e.g. tracking-api OOM), `docker compose up` leaves it exited and dependents stay unhealthy until a manual restart. For a dev/demo stack this is a convenience gap, not a correctness bug.
**Fix:** Optionally add `restart: unless-stopped` to the app services.

**Status:** out of scope for this fix pass (single-stack dev setup).

## Out-of-scope notes (not findings)

- D4 in 06-CONTEXT.md specified `curl -f` healthchecks for the .NET APIs; the implementation deviates to bash `/dev/tcp` because the aspnet:10.0 image has no curl. The deviation is correct and verified working (live stack healthy).
- `DeliveryConsumer.HandleBasicDeliverAsync`'s fire-and-forget `Task.Run` (line 145) is bounded by `prefetchCount: 10` and always acks on completion; unacked in-flight deliveries on shutdown are redelivered by design (at-least-once). No action.
- `HandleAsync`'s `Payload!.Value` null-`JsonElement` throws into the retry/DLQ path (`ShipmentEventsConsumer.cs:110-155`) — malformed payloads are routed, never escaped. Correct.

## Resolution Summary (2026-11-20)

Fixed: CR-01, MA-01, MA-02, MA-03, MI-02, MI-03. Open: MI-01. Skipped by design: MI-04 (D6), MI-05 (scope).

Gates re-run green after the fixes: `dotnet build KargoNet.sln` 0/0, `dotnet test` 12+79 passed, `docker compose up --build` all services healthy (compose defines 7 services; the "8" in phase docs is a miscount), operator-ui `npm run build -- --configuration docker` + `npm test` 26/26.


---

_Reviewed: 2026-11-20_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: deep_
