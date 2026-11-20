# KargoNet

## What This Is

KargoNet is a shipment tracking microservices system built on .NET 10. It consists of a Shipment service and a Tracking service that exchange shipment events over RabbitMQ (with hand-built retry and dead-letter queues), backed by PostgreSQL and a Redis cache. A customer-facing ASP.NET Core MVC portal lets customers create shipments and track them by tracking number. An Angular operator screen provides live shipment status updates over SignalR. Everything runs with Docker Compose.

## Core Value

Reliable shipment status propagation: a status change made in the operator UI must reach the customer portal and the operator's live view without loss, even when RabbitMQ or individual consumers are temporarily down.

## Requirements

### Validated

- ✓ Solution scaffold: KargoNet.sln, all projects, Angular app, .gitignore — Phase 1
- ✓ Contracts: ShipmentCreated, ShipmentStatusChanged payload records + ShipmentStatus enum + event envelope — Phase 2
- ✓ Messaging: RabbitMQ topology (exchange, main/retry 5s-TTL/DLQ queues), publisher with confirms, consumer base with retry and dead-letter logic — Phase 2
- ✓ Shipment.Api: EF Core model (Shipment, OutboxMessage), InitialCreate migration, REST endpoints (POST/GET/PUT), outbox publisher background service — Phase 3
- ✓ Tracking.Api: event consumer with idempotency (ProcessedEvents), read model (ShipmentTracking, TrackingEvents), Redis cache-aside, SignalR hub, DLQ admin endpoints — Phase 4
- ✓ Portal.Web: MVC customer portal with tracking search, timeline view, create-shipment form — Phase 5
- ✓ Operator UI: Angular app with shipments table, status-update action, SignalR live updates, timeline detail drawer — Phase 5
- ✓ Toolchain bootstrap: Docker installed and working (.NET 10 SDK validated Phase 1) — Phase 6
- ✓ Operator UI nginx proxy (Docker packaging) — Phase 6
- ✓ Docker: multi-stage Dockerfiles, docker-compose.yml with healthchecks, Postgres init script, .env — Phase 6
- ✓ Tests and README: xUnit tests for retry, idempotency, cache logic; README with run and demo steps; end-to-end verification — Phase 6

### Out of Scope

- Authentication / authorization — customers look up by tracking number; operator screen is open
- MassTransit or other messaging abstractions — retries and DLQ are hand-built per the plan
- Mobile apps — web-only
- Multi-tenant / multi-region deployment — single Docker Compose stack

## Context

- The full implementation plan lives in `PLAN.md` at the project root — architecture diagram, solution layout, key mechanics, Docker Compose services, and verification steps.
- Greenfield project: no existing code.
- Target environment: Linux, Docker Compose for local development and verification.
- .NET 10 SDK and Docker need to be installed (not present at project start).
- Node 24 and npm 11 are available for the Angular app.

## Constraints

- **Tech stack**: .NET 10, EF Core + Npgsql, RabbitMQ.Client 7.x (async), PostgreSQL 17, Redis 7, Angular (latest via npx), SignalR — fixed by plan
- **Messaging**: Hand-built retry/DLQ (3 retries, 5s TTL) — no middleware abstraction
- **Auth**: None — no login, no API keys
- **Deployment**: Single Docker Compose stack — no Kubernetes, no cloud provider
- **Toolchain**: .NET 10 SDK and Docker must be installed as part of the first phase

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| .NET 10 with EF Core + Npgsql | SDK target from plan; Npgsql is the standard EF provider for PostgreSQL | ✓ Phase 1 |
| RabbitMQ.Client 7.x async API, hand-built retry/DLQ | Plan requires explicit control over retry count, TTL, and DLQ routing; no middleware abstraction | ✓ Phase 2 |
| Outbox pattern for reliable publishing | Status change and event row saved in one EF transaction; background publisher polls and publishes with confirms; events survive RabbitMQ downtime | ✓ Phase 3 |
| Idempotency via ProcessedEvents table | Consumer checks EventId before applying, in the same transaction as the read-model update | ✓ Phase 4 |
| Redis cache-aside with 10-min TTL | Tracking lookups are read-heavy; cache deleted/overwritten on event consumption | ✓ Phase 4 |
| SignalR for operator live updates | Real-time push to Angular without polling; broadcasts to all operators + per-tracking-number groups | ✓ Phase 4 |
| No authentication | Plan explicitly excludes auth; customers track by number, operator screen is open | ✓ Phase 1 |
| Docker Compose for all infrastructure | Single-command local dev; healthchecks gate service startup ordering | ✓ Phase 6 |
| Classic .sln (not .slnx) | .NET 10 defaults to .slnx; plan/ROADMAP pin KargoNet.sln | ✓ Phase 1 |
| Client-side Angular SPA (--ssr false) | Operator screen is a live-updating dashboard; SSR buys nothing, keeps Dockerfile simple | ✓ Phase 1 |
| Server-rendered MVC portal (no SPA) | Customers track by number; server rendering keeps the customer side simple and Dockerfile-free | ✓ Phase 5 |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd-transition`):
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone** (via `/gsd:complete-milestone`):
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-11-20 after Phase 6*
