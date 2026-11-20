# Phase 6: Docker, Tests & Documentation — Context

**Phase goal:** The complete system runs as a single Docker Compose stack, core logic is tested, and the system is documented for end-to-end use.
**Requirements:** PLAT-01..05 · **Mode:** autonomous (all questions resolved with safe defaults)

## Locked Decisions

### D1 — Base images (frozen by AGENTS.md + verified availability)

| Service | Build stage | Runtime stage |
|---|---|---|
| shipment-api | `mcr.microsoft.com/dotnet/sdk:10.0` | `mcr.microsoft.com/dotnet/aspnet:10.0` |
| tracking-api | `mcr.microsoft.com/dotnet/sdk:10.0` | `mcr.microsoft.com/dotnet/aspnet:10.0` |
| portal-web | `mcr.microsoft.com/dotnet/sdk:10.0` | `mcr.microsoft.com/dotnet/aspnet:10.0` |
| operator-ui | `node:24` | `nginx:stable-alpine` |
| infra | `postgres:17`, `rabbitmq:4-management`, `redis:7` | — |

### D2 — Multi-stage .NET Dockerfiles (PLAT-02)

Three identical-shape Dockerfiles (`src/Services/Shipment.Api/Dockerfile`, `src/Services/Tracking.Api/Dockerfile`, `src/Web/Portal.Web/Dockerfile`):
1. `sdk` stage: `dotnet restore` (workdir = solution root copied in), `dotnet publish -c Release -o /app/publish` for the specific project.
2. `runtime` stage (`aspnet:10.0`): copy publish dir to `/app`, run as the built-in `app` user, `EXPOSE` the service port, `ENTRYPOINT ["/app/<Assembly>.dll"]`.
No `dotnet ef` in the runtime image — migrations run in-process (D7).

### D3 — operator-ui Dockerfile + nginx proxy (PLAT-02)

1. `node:24` stage: `npm ci` + `ng build --configuration production` with `--fileReplacements` swapping `environment.ts` → `environment.docker.ts` (relative API bases: `shipmentApiUrl: ''`, `trackingApiUrl: ''` — SPA then calls `/api/...` and `/hubs/...` on its own origin).
2. `nginx:stable-alpine` stage: copy `dist/operator-ui/browser` → `/usr/share/nginx/html`; custom `nginx.conf` proxies:
   - `location /api/` → `http://shipment-api:5001`
   - `location /hubs/` → `http://tracking-api:5002` (WebSocket `Upgrade` headers for SignalR)
Dev `environment.ts` (absolute `localhost:5001/5002`) untouched — `ng serve` workflow unchanged.

### D4 — Compose stack shape (PLAT-01)

`docker-compose.yml` at repo root, 8 services, fixed ports:

| Service | Image/build | Host port | Depends on (condition: service_healthy) |
|---|---|---|---|
| postgres | `postgres:17` | 5432 | — |
| rabbitmq | `rabbitmq:4-management` | 5672, 15672 | — |
| redis | `redis:7` | 6379 | — |
| shipment-api | build | 5001 | postgres, rabbitmq |
| tracking-api | build | 5002 | postgres, rabbitmq, redis |
| portal-web | build | 5000 | shipment-api, tracking-api |
| operator-ui | build | 4200 | shipment-api, tracking-api |

Healthchecks:
- postgres: `pg_isready -U $POSTGRES_USER`
- rabbitmq: `rabbitmq-diagnostics -q ping`
- redis: `redis-cli ping`
- .NET APIs: `curl -f http://localhost:<port>/health` (aspnet image has curl)
- operator-ui: `wget -qO- http://localhost:80/ > /dev/null` (busybox wget)

All `depends_on` use `condition: service_healthy` so startup ordering is gated on real readiness.

### D5 — Postgres init script (PLAT-03)

`docker/postgres/init.sql` mounted at `/docker-entrypoint-initdb.d/`:
```sql
CREATE DATABASE shipments;
CREATE DATABASE tracking;
```
Creds from `.env` (`POSTGRES_USER`, `POSTGRES_PASSWORD`); init runs only on first volume init (standard Postgres behavior).

### D6 — .env (committed, no real secrets)

`.env` at repo root (git-tracked; local dev only):
`POSTGRES_USER`, `POSTGRES_PASSWORD`, `RABBITMQ_DEFAULT_USER`, `RABBITMQ_DEFAULT_PASS`, and the connection strings used by the compose file. `.gitignore` keeps `*.env` out **except** the root `.env` (negation `!/.env`) — the repo ships a working default stack, consistent with PLAN.md listing `.env` in the solution layout.

### D7 — Migrations at startup (code change, required for SC1)

Both API `Program.cs` files gain an in-process migration step before starting the hosted services: `await dbContext.Database.MigrateAsync()` inside a bounded retry loop (10 attempts, 3 s apart) so the container survives Postgres accepting connections before it is ready. This is additive — no endpoint/DTO/config-surface change (frozen surfaces stay intact). Without it, empty compose databases have no tables and every request 500s.

### D8 — Seeding

No seed service. Demo shipments are created through the portal (SC5 flow). PLAN.md's "optional seed step" is declined: it adds a code path no requirement asks for.

### D9 — PLAT-04 test coverage (no new tests expected)

Existing 90/90 suite already covers:
- **retry decisions** — `RetryPolicy.Decide` + consumer retry/DLQ routing tests (Phase 2/4)
- **idempotency** — `ShipmentEventHandler` duplicate-EventId + race tests (Phase 4)
- **cache invalidation** — `RedisTrackingCache` + cache-aside endpoint tests (Phase 4)

Executor performs a coverage check against these three named areas and adds tests only for genuine gaps; the plan assumes zero new tests.

### D10 — E2E verification procedure (SC5)

1. `docker compose up --build` → all 8 services `healthy`
2. `GET http://localhost:5000/` → portal home
3. Create shipment via portal form (`:5000/shipments/new`) → 201, appears in operator table `:4200`
4. Update status via operator UI → `PUT :5001/api/shipments/{id}/status`
5. Portal `:5000/track?number=...` shows updated status + timeline entry; operator row badge updates live (SignalR)
6. Bonus demo (PLAN.md verification): poison a message → watch `x-retry-count` increment in RabbitMQ UI (`:15672`) → DLQ → `POST :5002/api/admin/dlq/replay`

### D11 — README (PLAT-05)

Root `README.md`: project overview + architecture diagram (from PLAN.md), prerequisites (Docker + Compose only), quick start (`docker compose up --build`, port table), demo walkthrough (D10 steps), non-Docker dev mode (host `dotnet run` + `ng serve` + local infra note), project layout, test instructions (`dotnet test` / `npm test`), DLQ admin endpoints.

## Open Items for Planning

- Task/plan split: (a) Dockerfiles + compose + init + .env + Migrate() wiring, (b) operator-ui nginx + environment.docker.ts, (c) README + test-coverage verification + E2E run.
- E2E run requires the live stack — executor runs `docker compose up --build` itself (Docker verified working this session).
