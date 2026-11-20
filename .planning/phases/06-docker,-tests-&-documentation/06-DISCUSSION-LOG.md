# Phase 6: Docker, Tests & Documentation — Discussion Log

Autonomous mode: each question resolved with the safe default; no user prompts sent.

## Q1 — How do services get database tables in a fresh compose stack?
**Chosen:** In-process `Database.MigrateAsync()` at API startup with a bounded retry loop (D7).
**Rationale:** Postgres init scripts only create databases, not tables; the runtime image has no `dotnet ef`. In-process migration is the standard .NET container pattern and keeps migrations in one source of truth. Additive Program.cs change only — no API surface change.

## Q2 — operator-ui API URLs in the container
**Chosen:** `environment.docker.ts` with relative bases via `--fileReplacements`; nginx proxies `/api` → shipment-api, `/hubs` → tracking-api (D3).
**Rationale:** environment.ts is baked at build time; hardcoding container hostnames into the SPA would leak internal names and break same-origin expectations. Relative + proxy is the PLAN.md design ("nginx proxy") and keeps the dev environment untouched.

## Q3 — .env: commit or gitignore?
**Chosen:** Commit the root `.env` with default local creds; `.gitignore` negation `!/.env` (D6).
**Rationale:** PLAN.md lists `.env` in the solution layout; the project has no auth and no real secrets. A committed working `.env` makes `docker compose up` one command. Documented in README as "change for anything shared".

## Q4 — Healthcheck for operator-ui (nginx alpine has no curl)?
**Chosen:** `wget -qO- http://localhost/ > /dev/null` — busybox wget ships in alpine (D4).

## Q5 — Seeding demo data?
**Chosen:** No seed service (D8).
**Rationale:** PLAN.md marks it optional; no requirement asks for it; the SC5 demo creates shipments through the portal, which exercises the real path.

## Q6 — New tests for PLAT-04?
**Chosen:** Gap analysis first; existing 78 Tracking tests already cover the three named areas (retry decisions, idempotency, cache invalidation). Add tests only for verified gaps (D9).
**Rationale:** Re-testing what Phases 2/4 already prove wastes budget; the requirement is coverage, not test count.

## Q7 — depends_on: `service_started` or `service_healthy`?
**Chosen:** `service_healthy` everywhere (D4).
**Rationale:** SC1 requires healthchecks passing and SC5 requires the whole flow to work; started-only ordering would let portal-web race a not-ready API.

## Q8 — RabbitMQ image tag
**Chosen:** `rabbitmq:4-management` (D1).
**Rationale:** PLAT-01 fixes "RabbitMQ 4 (management UI on 15672)".

## Q9 — .NET runtime user in containers?
**Chosen:** Built-in `app` user of the `aspnet` image (D2).
**Rationale:** Non-root by default; the image is designed for it (workdir `/app`, ownership pre-set).

## Q10 — E2E verification: browser or HTTP?
**Chosen:** HTTP-level E2E via `curl` against the running compose stack (D10); browser-level interaction is covered by Phase 5 vitest specs.
**Rationale:** No Docker-gated browser session needed; the live claim in SC5 is the data flow (create → update → timeline), which curl proves end to end including the SignalR-triggered state via API observation.
