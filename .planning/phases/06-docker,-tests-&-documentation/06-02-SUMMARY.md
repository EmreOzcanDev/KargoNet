---
phase: 06-docker,-tests-&-documentation
plan: 02
subsystem: frontend
tags: [docker, nginx, angular, signalr]
requirements: [PLAT-02]
depends_on: [06-01]
provides:
  - operator-ui container image (node:24 build + nginx:stable-alpine runtime)
  - same-origin /api/ and /hubs/ reverse proxy
key_files:
  created:
    - src/Web/operator-ui/Dockerfile
    - src/Web/operator-ui/nginx.conf
    - src/Web/operator-ui/src/environments/environment.docker.ts
  modified:
    - src/Web/operator-ui/angular.json
decisions:
  - "environment.docker.ts uses empty (relative) API bases so the SPA calls /api/ and /hubs/ on its own origin (D3)"
  - "angular.json 'docker' configuration carries the production budgets/outputHashing plus fileReplacements (Angular configs do not inherit)"
  - "nginx /hubs/ forwards WebSocket Upgrade headers + 3600s read timeout for SignalR (D3)"
  - "Dev environment.ts (absolute localhost URLs) and ng serve workflow untouched (D3)"
status: complete
metrics:
  duration: ~15m
  completed: 2026-11-20
actuals:
  tokens: 11000
  tasks: 2
  commits: 1
plan_head_before: 1ef1dca
plan_head_after: cbf2c3d
---

# Phase 06 Plan 02: operator-ui Nginx Proxy + Docker Environment Summary

**Containerized the Angular operator-ui as a two-stage image (node:24 build with fileReplacements to environment.docker.ts, nginx:stable-alpine runtime) that serves the SPA and reverse-proxies `/api/` → shipment-api:5001 and `/hubs/` (WebSocket) → tracking-api:5002, leaving the dev workflow unchanged.**

## Tasks Completed

| Task | Description | Commit |
|------|-------------|--------|
| 1 | environment.docker.ts (empty/relative API bases) + angular.json `docker` build configuration with fileReplacements | cbf2c3d |
| 2 | nginx.conf (SPA fallback + /api/ and /hubs/ proxies with WebSocket upgrade) + two-stage operator-ui Dockerfile | cbf2c3d |

## Key Implementation Details

**environment.docker.ts (D3):** Exports the same `Environment` interface as the dev `environment.ts` but with `shipmentApiUrl: ''` and `trackingApiUrl: ''`, so the SPA issues same-origin `/api/...` and `/hubs/...` requests that nginx routes to the compose services.

**angular.json (D3):** Added a `docker` configuration under `architect.build.configurations`. Because Angular build configurations do not inherit from one another, the entry explicitly carries the production budgets + `outputHashing: "all"` and adds `fileReplacements` mapping `src/environments/environment.ts` → `src/environments/environment.docker.ts`. The existing `production`, `development`, and `serve` configurations are untouched — `ng serve` and local `ng build --configuration production` keep using the absolute-localhost `environment.ts`.

**nginx.conf (D3):** Single server on :80, root `/usr/share/nginx/html`, SPA fallback `try_files $uri /index.html`. `location /api/` → `proxy_pass http://shipment-api:5001` (no URI part, so the full original path including `/api` is forwarded). `location /hubs/` → `proxy_pass http://tracking-api:5002` with `proxy_http_version 1.1`, `Upgrade`/`Connection "upgrade"` headers, and `proxy_read_timeout 3600s` for long-lived SignalR WebSockets. Standard `Host`/`X-Real-IP`/`X-Forwarded-For` headers forwarded. Installed as `/etc/nginx/conf.d/default.conf`, replacing the default site.

**Dockerfile (D3):** Stage 1 `node:24` — `npm ci`, copy project, `npx ng build --configuration docker`. Stage 2 `nginx:stable-alpine` — copy `dist/operator-ui/browser` → `/usr/share/nginx/html`, copy nginx.conf → `conf.d/default.conf`, EXPOSE 80.

## Verification

- `npm run build -- --configuration docker` → exit 0, emits `dist/operator-ui/browser`; built bundle contains no `localhost:5001` (grep of the browser output finds nothing) — environment.docker.ts confirmed swapped in
- `npx vitest run` is not the project's runner; `npm test` (Angular `ng test`, vitest under the hood) → 26/26 passed
- `sudo docker build -f src/Web/operator-ui/Dockerfile -t kargonet-operator-ui:local src/Web/operator-ui` → exit 0
- Run container serves the SPA at `/` with HTTP 200 (KargoNet Operator index.html). Note: standalone runs require the compose service hostnames to resolve (nginx resolves upstreams at config load); verified with `--add-host shipment-api/tracking-api` mappings — inside the compose network these names resolve natively.

## Deviations from Plan

- **Runner wording:** The plan's gate referenced `npx vitest run`, but the project's test script is `ng test` (Angular's unit-test builder wrapping vitest). `npx vitest run` alone fails with `describe is not defined` because Angular's globals/config are applied by the builder. Tests were run via `npm test` → 26/26. No behavioral change; same test suite, correct runner.

## Self-Check: PASSED
