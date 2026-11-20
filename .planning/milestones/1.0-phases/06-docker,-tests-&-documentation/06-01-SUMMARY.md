---
phase: 06-docker,-tests-&-documentation
plan: 01
subsystem: platform
tags: [docker, compose, migration]
requirements: [PLAT-01, PLAT-02, PLAT-03]
depends_on: []
provides:
  - docker-compose stack (8 services)
  - .NET service Dockerfiles
  - in-process startup migration
key_files:
  created:
    - src/Services/Shipment.Api/Dockerfile
    - src/Services/Tracking.Api/Dockerfile
    - src/Web/Portal.Web/Dockerfile
    - .dockerignore
    - docker-compose.yml
    - docker/postgres/init.sql
    - .env
  modified:
    - .gitignore
    - src/Services/Shipment.Api/Program.cs
    - src/Services/Tracking.Api/Program.cs
decisions:
  - "Multi-stage sdk:10.0 -> aspnet:10.0 images, built-in app user (D2)"
  - "All depends_on use condition: service_healthy (D4)"
  - "MigrateAsync 10x3s bounded retry, skipped when Database:Provider == InMemory (D7)"
plan_head_before: 00da569f06881624100917491ff71911323068eb
status: complete
metrics:
  duration: ~25m
  completed: 2026-11-20
actuals:
  tokens: 22000
  tasks: 3
  commits: 1
plan_head_before: 1ef1dca^
plan_head_after: 1ef1dca
---

# Phase 06 Plan 01: Docker Compose Stack, Dockerfiles, Migrate() Startup Summary

**Containerized the three .NET services and defined the full 8-service Compose stack with readiness-gated healthchecks, Postgres init, committed .env, and in-process EF migrations at startup (D7).**

## Tasks Completed

| Task | Description | Commit |
|------|-------------|--------|
| 1 | Three multi-stage .NET Dockerfiles (sdk:10.0 → aspnet:10.0, app user, correct EXPOSE/ENTRYPOINT) + root .dockerignore | 1ef1dca |
| 2 | docker-compose.yml (8 services, service_healthy ordering, D4 healthchecks), docker/postgres/init.sql, .env, .gitignore negation | 1ef1dca |
| 3 | In-process MigrateAsync with 10×3 s bounded retry in both API Program.cs, skipped in InMemory mode | 1ef1dca |

## Key Implementation Details

**Dockerfiles (D2):** Identical shape across Shipment.Api, Tracking.Api, Portal.Web — stage 1 `sdk` (`mcr.microsoft.com/dotnet/sdk:10.0`) restores + publishes the specific project to `/app/publish`; stage 2 `runtime` (`mcr.microsoft.com/dotnet/aspnet:10.0`) copies publish dir to `/app`, runs as `app`, EXPOSE 5001/5002/5000, ENTRYPOINT the respective DLL. No `dotnet ef` in runtime images.

**docker-compose.yml (D4):** 8 services — postgres (17, :5432), rabbitmq (4-management, :5672/:15672), redis (7, :6379), shipment-api (:5001), tracking-api (:5002), portal-web (:5000), operator-ui (:4200). Every `depends_on` uses `condition: service_healthy`. Healthchecks per D4: `pg_isready -U`, `rabbitmq-diagnostics -q ping`, `redis-cli ping`, `curl -f …/health` for .NET, `wget -qO- http://localhost/` for operator-ui. The .NET services build with `context: .` + project Dockerfile so the repo-root .dockerignore applies.

**init.sql (D5):** `CREATE DATABASE shipments;` + `CREATE DATABASE tracking;`, mounted read-only at `/docker-entrypoint-initdb.d/init.sql`.

**.env (D6):** Committed with default local creds (POSTGRES_USER/PASSWORD, RABBITMQ_DEFAULT_USER/PASS, and the connection strings consumed by compose: ShipmentDb, TrackingDb, Redis, RabbitMq). `.gitignore` gained `*.env` + `!/.env` so the root .env is tracked while other env files stay ignored.

**MigrateAsync (D7):** Both API `Program.cs` files run, after `builder.Build()` and before `app.Run()`, a bounded retry loop (10 attempts, 3 s apart) that creates a scope, resolves the DbContext, and calls `Database.MigrateAsync()`. Guarded by the existing `useInMemory` flag so `WebApplicationFactory` (InMemory) tests are unaffected. On final failure the exception propagates so the container goes unhealthy.

## Verification

- `sudo docker compose config -q` → exit 0
- `~/.dotnet/dotnet build KargoNet.sln` → 0 errors (8 pre-existing nullability warnings in test files, unchanged)
- `~/.dotnet/dotnet test` → 90/90 passed (Portal 12 + Tracking 78)
- All three .NET Dockerfiles build standalone (`kargonet-shipment-api:local`, `kargonet-tracking-api:local`, `kargonet-portal-web:local`)
- `git check-ignore .env` → exit 1 (not ignored); `git status` shows `?? .env` (trackable)

## Deviations from Plan

None — plan executed exactly as written.

## Self-Check: PASSED
