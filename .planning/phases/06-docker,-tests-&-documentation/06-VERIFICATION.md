---
phase: 06-docker,-tests-&-documentation
verified: 2026-11-20T12:42:47Z
status: passed
score: 5/5
behavior_unverified: 0
overrides_applied: 0
covered_files:
  - ".dockerignore"
  - ".env"
  - ".gitignore"
  - ".planning/phases/06-docker,-tests-&-documentation/06-01-PLAN.md"
  - ".planning/phases/06-docker,-tests-&-documentation/06-01-SUMMARY.md"
  - ".planning/phases/06-docker,-tests-&-documentation/06-02-PLAN.md"
  - ".planning/phases/06-docker,-tests-&-documentation/06-02-SUMMARY.md"
  - ".planning/phases/06-docker,-tests-&-documentation/06-03-PLAN.md"
  - ".planning/phases/06-docker,-tests-&-documentation/06-03-SUMMARY.md"
  - "README.md"
  - "docker-compose.yml"
  - "docker/postgres/init.sql"
  - "src/BuildingBlocks/KargoNet.Messaging/Consuming/RabbitConsumerBroker.cs"
  - "src/Services/Shipment.Api/Dockerfile"
  - "src/Services/Shipment.Api/Program.cs"
  - "src/Services/Tracking.Api/Dockerfile"
  - "src/Services/Tracking.Api/Program.cs"
  - "src/Web/Portal.Web/Dockerfile"
  - "src/Web/operator-ui/Dockerfile"
  - "src/Web/operator-ui/angular.json"
  - "src/Web/operator-ui/nginx.conf"
  - "src/Web/operator-ui/src/environments/environment.docker.ts"
  - "tests/KargoNet.Tracking.Tests/Consuming/ShipmentEventsConsumerHostedTests.cs"
  - "tests/KargoNet.Tracking.Tests/Consuming/ShipmentEventsConsumerTests.cs"
  - "tests/KargoNet.Tracking.Tests/Dlq/RabbitDlqAdminTests.cs"
  - "tests/KargoNet.Tracking.Tests/Messaging/LazyChannelFactoryTests.cs"
  - "tests/KargoNet.Tracking.Tests/Messaging/RabbitConsumerBrokerTests.cs"
covered_digest: "v4:sha256:7d0a2cbf596e75588a68476f9961022e86087652688a8149d1e823fe110d146c"
---
# Phase 6: Docker, Tests & Documentation — Verification Report

**Phase Goal:** The full system runs as a single Docker Compose stack, the core logic is tested, and the system is documented end-to-end.
**Verified:** 2026-11-20T12:42:47Z
**Status:** PASSED
**Re-verification:** No — initial verification

## Goal Achievement

### Success Criteria

| # | Criterion | Verdict | Evidence |
|---|-----------|---------|----------|
| SC1 | `docker compose up --build` starts all services with healthchecks passing | **VERIFIED** | Live `docker compose ps`: 7/7 `healthy` (postgres:17, rabbitmq:4-management, redis:7, shipment-api, tracking-api, portal-web, operator-ui). `docker-compose.yml` defines healthchecks for every service (pg_isready / rabbitmq-diagnostics / redis-cli ping / `bash /dev/tcp` probes on `/health` for the 3 .NET services / `wget` for operator-ui) and all `depends_on` use `condition: service_healthy` (lines 12–122). Note: phase docs say "8 services" — a miscount; compose and the CONTEXT D4 table both define 7, and fix commit `68826af` corrected the summary to 7/7. |
| SC2 | Postgres init script creates both `shipments` and `tracking` databases | **VERIFIED** | `docker/postgres/init.sql` (2 lines): `CREATE DATABASE shipments;` + `CREATE DATABASE tracking;`, mounted at `docker-entrypoint-initdb.d/init.sql` (compose line 11). Live proof: `docker compose exec postgres psql -U kargonet -l` lists both `shipments` and `tracking` databases. |
| SC3 | `dotnet test` passes all xUnit tests covering consumer retry decisions, idempotency, and cache invalidation | **VERIFIED** | Verifier ran the full suite once: **91/91 passed, 0 failed** (KargoNet.Portal.Tests 12 + KargoNet.Tracking.Tests 79, wall time ~10s). Named coverage confirmed in source: `RetryPolicyTests.FirstFailure_RepublishesWithRetryCountOne` / `ThirdFailure_RoutesToDlq`; `ShipmentEventsConsumerTests.Handler_failure_routes_to_retry_then_dlq_and_always_ackd`, `Redelivered_same_event_id_is_applied_exactly_once_and_ackd`, `Race_backstop_committed_marker_is_ackd_as_success_without_retry`; `TrackingEndpointTests.Second_get_is_served_from_cache_not_the_database`. (Suite is 91, not the 90 in summaries — the +1 is the MA-01 cancel-path test from `RabbitConsumerBrokerTests`.) |
| SC4 | README documents how to build, run, and demo the system end-to-end | **VERIFIED** | `README.md` contains: overview + mermaid architecture diagram, prerequisites, quick start (`docker compose up --build`) with 7-row port table, 6-step demo walkthrough (D10) incl. DLQ poison → replay curl, dev-mode section (with the 4 env-var table), project layout, test instructions with the three PLAT-04 areas mapped to named tests, and the DLQ admin endpoints (`GET /api/admin/dlq`, `POST /api/admin/dlq/replay`). |
| SC5 | End-to-end flow verified: create in portal → update in operator UI → portal timeline updates live | **VERIFIED** | Recorded in `06-03-SUMMARY.md` (D10): portal form POST → 302 → `/track?number=E2E-1795172045`; status PUTs (PickedUp/InTransit/Delivered) → 200; tracking DB `ShipmentTrackings`/`TrackingEvents` rows; timeline via `GET :5002/api/tracking/E2E-1795172045/events`; **live SignalR over WebSockets through nginx `:4200/hubs/tracking`** (2× `ShipmentUpdated` frames received with the official `@microsoft/signalr` client); DLQ poison → retries → `{"count":5}` → replay `{"replayed":5}` → count 0. Live residue re-confirmed by verifier: `:4200/api/tracking/E2E-1795172045` returns `status: Delivered`; `/events` returns the full status-changed timeline; `POST :4200/hubs/tracking/negotiate` → 200 with WebSockets transport advertised (proves the `/hubs/` proxy + transport path is live). |

### Platform Requirements

| # | Requirement | Verdict | Evidence |
|---|-------------|---------|----------|
| PLAT-01 | Compose stack: Postgres 17, RabbitMQ 4 (mgmt 15672), Redis 7, each with healthchecks | **VERIFIED** | `docker-compose.yml`: `postgres:17` (5432), `rabbitmq:4-management` (5672 + 15672), `redis:7` (6379), each with a healthcheck; all live and healthy. |
| PLAT-02 | Multi-stage Dockerfiles for shipment-api, tracking-api, portal-web, operator-ui | **VERIFIED** | All four exist in the required shape: three .NET Dockerfiles are `sdk:10.0 → aspnet:10.0` with `USER app`, `EXPOSE`, `ENTRYPOINT ["dotnet", "/app/<X>.dll"]`; operator-ui is `node:24 → nginx:stable-alpine` copying `dist/operator-ui/browser` → `/usr/share/nginx/html` + `nginx.conf` → `conf.d/default.conf`. Live: all containers run from built `kargonet-*` images. |
| PLAT-03 | Postgres init script creates both databases | **VERIFIED** | Same evidence as SC2. |
| PLAT-04 | xUnit tests cover retry decisions, idempotency, cache invalidation | **VERIFIED** | Same evidence as SC3; all 6 phase-6 test files present (`Consuming/ShipmentEventsConsumerTests.cs`, `Consuming/ShipmentEventsConsumerHostedTests.cs`, `Dlq/RabbitDlqAdminTests.cs`, `Messaging/LazyChannelFactoryTests.cs`, `Messaging/RabbitConsumerBrokerTests.cs` + pre-existing areas). |
| PLAT-05 | README documents build, run, demo end-to-end | **VERIFIED** | Same evidence as SC4. |

### Supporting Artifacts (all verified present and substantive)

| Artifact | Details |
|---|---|
| `docker-compose.yml` | 7 services, health-gated `depends_on`, per-service healthchecks, `ASPNETCORE_URLS` on all 3 .NET services, full env wiring (`ConnectionStrings__ShipmentDb/__TrackingDb/__RabbitMq`, `ConnectionStrings__Redis`, `ShipmentApiUrl`, `TrackingApiUrl`) |
| `docker/postgres/init.sql` | Exactly the two `CREATE DATABASE` lines |
| `.env` | kargonet/kargonet creds + 4 connection strings consumed by Compose |
| `.gitignore` | `*.env` with `!/.env` negation (D6 — committed with safe defaults) |
| `.dockerignore` | Excludes `.git`, `.planning`, `bin`, `obj`, `node_modules`, `.angular`, `tests`, `.env` |
| 3× .NET `Dockerfile` | Multi-stage, non-root, documented `dotnet <dll>` entrypoint (deviation 1 fix present) |
| `operator-ui/Dockerfile`, `nginx.conf`, `environment.docker.ts`, `angular.json` | `ng build --configuration docker` (fileReplacements at angular.json:52,66); empty docker env bases; nginx routes `/api/tracking/` → tracking-api, `/api/` → shipment-api, `/hubs/` → tracking-api with WebSocket upgrade, `proxy_read_timeout 3600s`, `proxy_buffering off`, SPA fallback |
| `Shipment.Api/Program.cs`, `Tracking.Api/Program.cs` | `MigrateAsync` in a 10-attempt loop, guarded by `useInMemory`, with per-attempt warning logging (D7 + MI-02 fix) |
| `RabbitConsumerBroker.cs` | `ConsumeAsync` blocks on a channel-shutdown TCS; `ct.Register(() => channelShutdown.TrySetCanceled())` (MA-01 fix, line 107) |
| `README.md` | All D11 sections |

### Live Spot-Checks (read-only, this verification)

| Check | Result |
|---|---|
| `docker compose ps` | 7/7 healthy |
| `GET :5000/` | 200 (portal home) |
| `GET :5001/health` | `{"status":"healthy",…7 statuses}` |
| `GET :5002/health` | `{"status":"healthy"}` |
| `GET :4200/` | 200 (SPA) |
| `GET :4200/api/shipments` | 200, real shipment rows (E2E residue) |
| `GET :4200/api/tracking/E2E-1795172045` | 200 — tracking data via nginx (CR-01 route live) |
| `GET :4200/api/tracking/E2E-1795172045/events` | 200 — full status-changed timeline |
| `POST :4200/hubs/tracking/negotiate` | 200 — WebSockets/SSE/LongPolling advertised through the proxy |
| `GET :5002/api/admin/dlq` | `{"count":3}` (poison replay residue, matches summary note) |
| `psql -U kargonet -l` | `shipments` + `tracking` databases present |

### Code Review Resolution (REVIEW.md)

| Finding | Status in REVIEW.md | Codebase check |
|---|---|---|
| CR-01 (`/api/tracking/` → tracking-api) | resolved, `30c551a` | ✓ `nginx.conf:11-17`; live route confirmed |
| MA-01 (consume TCS linked to caller ct) | resolved, `2943056` | ✓ `RabbitConsumerBroker.cs:107` |
| MA-02 (`proxy_buffering off`) | resolved, `30c551a` | ✓ `nginx.conf:36` |
| MA-03 (dev-mode truth in README) | resolved, `3ddaaff` | ✓ README "Development mode" section + env-var table |
| MI-02 (log failed migration attempts) | resolved, `cf98131` | ✓ both `Program.cs` log per-attempt warnings |
| MI-03 (ng test wording) | resolved, `3ddaaff` | ✓ README "ng test, vitest-backed" |
| MI-01 (broker cancel path) | **open — not in assigned fix scope** | ✓ open as documented: `HandleBasicCancelAsync` still returns `Task.CompletedTask` (lines 176–177); MA-01 covers the caller-side half |
| MI-04 (no healthcheck on dev flow) | not required (D6) | ✓ no action needed |
| MI-05 (no restart policy) | out of scope | ✓ no action needed |

All six referenced fix commits exist (`1ef1dca`, `cbf2c3d`, `30c551a`, `2943056`, `cf98131`, `3ddaaff`).

### Anti-Patterns

Debt-marker scan (`TBD|FIXME|XXX|TODO|HACK|PLACEHOLDER`) over all 21 phase-6 implementation files: **clean (zero matches)**. No stubs, no empty handlers in the phase file set.

### Advisories (non-blocking)

1. **README service count** — README line 44 ("All 8 services") and line 111 ("The 8-service stack") still carry the miscount; compose defines 7 and the README port table lists 7. Cosmetic; the summary was already corrected by `68826af`.
2. **MI-01 open by design** — broker `basic.cancel` still leaves the consume loop blocked until the channel dies or the caller's ct fires; deliberate scope decision recorded in REVIEW.md.
3. **MI-05 out of scope** — no Compose restart policies; documented in CONTEXT D4.

### Gaps

None.

---

## Overall Verdict

**PASSED — 10/10 criteria verified (SC1–SC5, PLAT-01..05).**

The phase goal is achieved in the codebase: the 7-service Compose stack is live with all healthchecks passing, both databases are created by the init script, the 91-test xUnit suite passes (verified by a fresh run of this verification), the README covers build/run/demo/dev/test end-to-end, and the E2E flow (portal create → operator status update → live timeline + SignalR + DLQ replay) is backed by recorded evidence in `06-03-SUMMARY.md` plus live residue re-confirmed by this verifier. All resolved review findings are present in the code; the open/skipped ones match their documented dispositions.

_Verified: 2026-11-20T12:42:47Z_
_Verifier: gsd-verifier_
