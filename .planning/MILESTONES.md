# Milestones

## 1.0 1.0 (Shipped: 2026-11-20)

**Phases completed:** 6 phases, 18 plans, 35 tasks

**Key accomplishments:**
- .NET 10 SDK (10.0.404) installed, classic KargoNet.sln scaffolded with KargoNet.Contracts (ShipmentStatus enum) and Shipment.Api whose GET /health proves the Contracts→Service reference chain, plus root .gitignore
- Angular SPA scaffolded at src/Web/operator-ui via the official @angular/cli (post-legitimacy-gate), retargeted from the default counter template to a minimal "KargoNet Operator" shell, and proven to build with ng build (exit 0, dist/ present).
- KargoNet.sln now holds all six .NET projects with the architecture's reference graph (Tracking.Api → Contracts + Messaging, Portal.Web → Contracts), the whole solution builds clean, and the xUnit suite is green with a test pinning the seven ShipmentStatus lifecycle values.
- EventEnvelope + ShipmentCreated/ShipmentStatusChanged payload records with a camelCase System.Text.Json EventSerializer, proven by 4 round-trip unit tests
- Data-only TopologySpec + RabbitTopology.DeclareAsync declaring the locked topology (topic exchange, main/retry 5s-TTL/DLQ with dead-letter routing), RabbitMQ.Client pinned 7.2.2, proven by 5 broker-free unit tests
- MessagePublisher awaiting a publish confirm (throwing PublishNotConfirmedException on unconfirmed) over a thin IOutboxChannel seam, with RabbitOutboxChannel as the real 7.x adapter — 3 unit tests; live broker-ack deferred to Phase 6
- ConsumerBase<T> + pure RetryPolicy implementing the locked 3-attempt retry/DLQ semantics (republish attempts 1&2 with incremented x-retry-count; ≥3 → DLQ with x-exception; always ack, never requeue) — 12 unit tests
- Shipment/OutboxMessage entities, ShipmentDbContext with outbox index, and a design-time InitialCreate migration (EF Core 10.0.15 + Npgsql 10.0.3), proven by 2 model tests over InMemory
- Minimal API endpoints (create/list/get/status) where every mutation writes the outbox row in the same EF transaction as the domain row, proven by 6 WebApplicationFactory integration tests over InMemory
- OutboxPublisherService (2 s / batch-50 / per-row-confirm / leave-pending) plus LazyOutboxChannelFactory so the API starts without a reachable broker, with complete Program.cs DI — proven by 4 unit tests over InMemory + a fake IOutboxChannel
- EF Core read model (ShipmentTracking / TrackingEvent / ProcessedEvent) with a design-time InitialCreate migration, a one-transaction idempotent apply handler, and a ConsumerBase-mirroring delivery pipeline — proven by 8 unit tests over InMemory + fakes, no live services
- Redis-backed cache-aside tracking endpoints (StackExchange.Redis 2.13.23) and a DLQ admin surface (passive count + replay) over a new LazyChannelFactory — proven by 9 unit tests with InMemory EF, a fake cache, and a full IChannel fake, no live services
- SignalR tracking hub with per-tracking-number groups, a lazy RabbitConsumerBroker over the frozen IConsumerBroker surface, and the ShipmentEventsConsumer converted to a BackgroundService with a 2 s outage-retry consume loop — proven by 15 unit tests with an InMemory scope, fake channels, and .NET 10 SignalR fakes, no live services
- Razor customer portal with lookup, tracking timeline (shared rules 1–6), and create-shipment form, backed by `PortalApiClient` and proven by 12 controller tests over `FakePortalApiClient`
- Zoneless Angular 22 operator console: live shipments table, status Apply with per-row inline errors, connection indicator, and a timeline drawer with SignalR group join/leave + live re-fetch — 25 vitest unit tests green
- Containerized the three .NET services and defined the full 8-service Compose stack with readiness-gated healthchecks, Postgres init, committed .env, and in-process EF migrations at startup (D7).
- Containerized the Angular operator-ui as a two-stage image (node:24 build with fileReplacements to environment.docker.ts, nginx:stable-alpine runtime) that serves the SPA and reverse-proxies `/api/` → shipment-api:5001 and `/hubs/` (WebSocket) → tracking-api:5002, leaving the dev workflow unchanged.

---
