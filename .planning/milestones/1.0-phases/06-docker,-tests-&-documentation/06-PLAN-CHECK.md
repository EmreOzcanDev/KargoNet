# Phase 6 Plan Check — Docker, Tests & Documentation

**Checked:** 06-01-PLAN.md, 06-02-PLAN.md, 06-03-PLAN.md (commit f1aebef)
**Against:** ROADMAP.md Phase 6 (PLAT-01..05, SC1..SC5), 06-CONTEXT.md (D1–D11)
**Date:** 2026-11-20
**Verdict:** ✅ PASS — 0 blockers, 2 warnings, 5 advisories

---

## Coverage

### Requirements (PLAT)

| Requirement | Plan(s) | Status |
|---|---|---|
| PLAT-01 (compose stack) | 06-01 (Task 2, D4) | Covered |
| PLAT-02 (Dockerfiles) | 06-01 (Task 1, D2), 06-02 (D3) | Covered |
| PLAT-03 (Postgres init) | 06-01 (Task 2, D5) | Covered |
| PLAT-04 (tests) | 06-03 (Task 1, D9) | Covered |
| PLAT-05 (README) | 06-03 (Task 3, D11) | Covered |

All five requirement IDs appear in the `requirements` frontmatter of at least one plan. No roadmap requirement left uncovered.

### Success Criteria (SC)

| SC | Where proven | Status |
|---|---|---|
| SC1 — 8 services healthy | 06-03 Task 2 step 1 (`docker compose ps` 8/8 healthy) | Covered |
| SC2 — both DBs created | 06-01 Task 2 (init.sql, D5) + 06-03 Task 2 step 1 (`psql -l` verification) | Covered |
| SC3 — dotnet test green, 3 areas covered | 06-03 Task 1 (D9) | Covered |
| SC4 — README build/run/demo | 06-03 Task 3 (D11) | Covered |
| SC5 — E2E create → update → live timeline | 06-03 Task 2 (D10 steps 2–5) | Covered (see W-2 re: live-SignalR leg) |

### Locked Decisions (D1–D11)

| Decision | Implemented by | Status |
|---|---|---|
| D1 base images | 06-01 T1 (sdk:10.0/aspnet:10.0), 06-02 (node:24, nginx:stable-alpine), 06-01 T2 (postgres:17, rabbitmq:4-management, redis:7) | Compliant |
| D2 .NET multi-stage Dockerfiles | 06-01 T1 — exact shape (sdk publish → aspnet, USER app, EXPOSE, ENTRYPOINT) | Compliant |
| D3 operator-ui + nginx proxy | 06-02 T1/T2 — fileReplacements to environment.docker.ts, /api/ + /hubs/ (WebSocket upgrade) proxies, dev env untouched | Compliant |
| D4 8-service compose, healthchecks, service_healthy | 06-01 T2 — port table, healthchecks, all `condition: service_healthy` match D4 exactly | Compliant |
| D5 init.sql | 06-01 T2 — `CREATE DATABASE shipments; CREATE DATABASE tracking;` | Compliant |
| D6 .env committed + .gitignore negation | 06-01 T2 — verified `.gitignore` currently has no `*.env` line; plan correctly adds `*.env` then `!/.env` | Compliant |
| D7 in-process MigrateAsync 10×3 s, InMemory skip | 06-01 T3 — the only approved source change; matches D7 verbatim | Compliant |
| D8 no seed service | No plan introduces seeding; 06-03 T2 step 3 creates demo data through the portal API path (D10) | Compliant |
| D9 coverage check, no new tests assumed | 06-03 T1 — three named areas, tests only for genuine gaps | Compliant |
| D10 E2E procedure | 06-03 T2 — steps 1–6 mirror D10 | Compliant (see W-2) |
| D11 README sections | 06-03 T3 — all D11 sections listed | Compliant |

No task contradicts a locked decision; no deferred idea appears in any plan.

### File Overlap (zero-overlap requirement)

| Plan | files_modified |
|---|---|
| 06-01 | 10 files (3 Dockerfiles, .dockerignore, docker-compose.yml, init.sql, .env, .gitignore, 2 Program.cs) |
| 06-02 | 4 files (Dockerfile, nginx.conf, environment.docker.ts, angular.json) |
| 06-03 | 1 file (README.md) |

**Zero overlap between plans.** ✓

### Dependencies / Waves

- 06-01 wave 1 (`depends_on: []`), 06-02 wave 1 (`depends_on: []`), 06-03 wave 2 (`depends_on: [06-01, 06-02]`).
- No cycles, no forward references. Wave numbers consistent.
- 06-03 context references 06-01/02 SUMMARY.md — valid since it runs after both.

### Verification Gates — Runnability in This Environment

| Gate | Tool | Available? |
|---|---|---|
| `sudo docker build`, `docker compose config -q`, `compose up --build`, `compose ps`, `compose exec` | Docker 29.1.3 + Compose | ✅ verified installed |
| `~/.dotnet/dotnet build`, `dotnet test` | .NET SDK 10.0.404 | ✅ verified installed |
| `npm run build -- --configuration docker` | npm 11.19.1 | ✅ verified installed |
| `curl` / `wget` gates | standard | ✅ |

All automated gates are runnable as written. Plan 02 Task 1's gate (`npm run build -- --configuration docker` + bundle grep for absence of `localhost:5001`) is a sound environment-swap proof.

### Grounded Claims (read-only spot checks)

- Named test areas for D9 exist in `tests/KargoNet.Tracking.Tests/`: `RetryPolicyTests.cs`, `ShipmentEventsConsumerTests.cs` (retry/DLQ routing), `ShipmentEventsConsumerTests.cs` (idempotency), `Api/*EndpointTests.cs` + `Realtime/` (cache). Plan 03 T1's "assume zero new tests" is grounded.
- `Program.cs` health endpoints confirmed in Shipment.Api (line 29) and Tracking.Api (line 41); Portal.Web has no `/health` — plan 06-01 T2 already hedges this ("if it does not, use `curl -f http://localhost:5000/`"). ✓
- `src/Web/operator-ui/src/environments/` contains only `environment.ts`; plan 06-02 T1 correctly creates `environment.docker.ts` fresh and edits `angular.json` (exists). ✓

---

## Findings

### W-1 — [scope_sanity] WARNING
Plan 06-01 carries **10 files** across 3 tasks — at the upper warning threshold (5–8 comfortable, 10 borderline).
- Mitigating: tasks are cleanly separated (Dockerfiles / compose+env / Program.cs), each with its own verify; actions are specific.
- Example fix (non-binding): none required; executor should treat Task 2's four files as one atomic compose-env unit and keep Task 3 isolated so a partial failure doesn't straddle concerns.

### W-2 — [context_compliance] WARNING
06-03 Task 2 step 5 (SignalR live leg) offers a fallback — "if no WS client exists on the host, record the negotiate 200 + the API-observed state change as the SignalR-path proof (D10 permits API observation for the live claim)". **D10 as written in CONTEXT.md does not permit this:** step 5 requires the operator badge/portal to update *live* (SignalR), and the plan's parenthetical misquotes the decision.
- Primary path (WebSocket client via `websocat`/`wscat` against `:4200/hubs/tracking`) fully satisfies SC5 and is feasible here — `npm 11` is available, so `npx wscat` (or `npm i -g wscat`) provides a client without new tooling decisions.
- Example fix (non-binding): executor should install/`npx` a WS client and take the primary path; only fall back if that genuinely fails, and if it does, record the deviation in 06-03-SUMMARY.md rather than citing D10 as permission.

### I-1 — [dependency_correctness] INFO
Same-wave coupling between 06-01 and 06-02 (no `depends_on` between them): 06-01's `docker-compose.yml` references the `operator-ui` build context whose Dockerfile is delivered by 06-02. Non-blocking: 06-01's only gate is `docker compose config -q` (no build), and 06-03 (wave 2) builds the full stack after both. Acceptable as-is; a `coupling_justified` note would make the ordering explicit.

### I-2 — [task_completeness] INFO
06-03 Tasks 1 and 2 declare `<files>README.md</files>` although neither task writes the README (only Task 3 does). Harmless, but the file lists are misleading for the executor's change tracking.

### I-3 — [task_completeness] INFO
06-01 Task 1 has no `<name>` element (Tasks 2–3 do). Cosmetic.

### I-4 — [scope_sanity] INFO
06-02 builds with context `src/Web/operator-ui`; the root `.dockerignore` (06-01 T1) does not apply to that nested context, so `node_modules` from a prior local `npm install` would bloat the build context. Performance only — `npm ci` inside the image is unaffected.

### I-5 — [verification_derivation] INFO
06-03 Task 2 step 6 (DLQ bonus) is double-hedged ("if such a path exists … if available"). The fallback — manually publishing a malformed message to the events queue via `rabbitmqadmin` in `rabbitmq:4-management` — is a concrete, runnable path, so the bonus remains achievable. No action needed.

---

## Anti-Scope Checks

- Only approved source change (D7 in both `Program.cs`) is present in 06-01 T3; `angular.json`/`environment.docker.ts` are build-config additions explicitly mandated by D3, not app-code changes.
- No seed service (D8 declined), no new config keys, no endpoint/DTO surface changes.
- No deferred ideas from CONTEXT.md appear in any plan.
- Estimates present (06-01: 85k, 06-02: 45k, 06-03: 75k, confidence low) — within normal per-plan budget; 06-03's E2E task is the heaviest single task but stays procedural.

## Result

**PASS.** No blockers. 2 warnings (W-1 advisory-only; W-2 needs executor attention to take the primary WebSocket path for SC5) and 5 advisories. Plans are ready for execution.
