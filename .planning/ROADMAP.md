# Roadmap: KargoNet

## Milestones

- 🚧 **v1.0 KargoNet** — Shipment tracking microservices with RabbitMQ, SignalR, and Docker Compose

## Phases

- [ ] **Phase 1: Foundation & Scaffold** - Toolchain installed, solution structured, all projects created
- [ ] **Phase 2: Contracts & Messaging** - Event contracts, RabbitMQ topology, publisher with confirms, retry/DLQ consumer base
- [ ] **Phase 3: Shipment API** - REST endpoints for shipments with EF Core, outbox pattern, and background publisher
- [ ] **Phase 4: Tracking API** - Event consumer with idempotency, Redis cache-aside, SignalR hub, DLQ admin
- [ ] **Phase 5: Web Frontends** - MVC customer portal and Angular operator screen with live updates
- [ ] **Phase 6: Docker, Tests & Documentation** - Docker Compose stack, xUnit tests, README with demo steps

## Phase Details

### Phase 1: Foundation & Scaffold
**Goal**: Project is scaffolded with .NET 10 toolchain, complete solution structure, and all projects ready to build.
**Depends on**: Nothing (first phase)
**Requirements**: (none directly — enabling infrastructure)
**Success Criteria** (what must be TRUE):
  1. `dotnet build` succeeds on KargoNet.sln with all projects compiling
  2. Solution layout matches plan: src/BuildingBlocks, src/Services, src/Web, tests directories exist with their projects
  3. Angular app is scaffolded via npx and `ng build` succeeds
  4. .gitignore excludes bin/, obj/, node_modules/, .angular/
**Plans:** 3 plans

Plans:
- [ ] 01-01-PLAN.md — .NET 10 SDK + solution tracer (sln, Contracts, Shipment.Api /health, .gitignore)
- [ ] 01-02-PLAN.md — Angular operator-ui scaffold + npm legitimacy gate
- [ ] 01-03-PLAN.md — Remaining .NET projects (Messaging, Tracking.Api, Portal.Web) + xUnit test project

### Phase 2: Contracts & Messaging
**Goal**: Shipment events can be published and consumed over RabbitMQ with reliable delivery, hand-built retry, and dead-letter routing.
**Depends on**: Phase 1
**Requirements**: MSG-01, MSG-02, MSG-03, MSG-04
**Success Criteria** (what must be TRUE):
  1. RabbitMQ topology declares topic exchange, main queue, retry queue (5s TTL), and DLQ with proper dead-letter routing
  2. Publisher sends a message with publisher confirms and receives broker acknowledgment
  3. A failing consumer republishes to the retry queue with incremented x-retry-count header (attempts below 3)
  4. A message exceeding 3 retry attempts is routed to the DLQ with x-exception header
**Plans**: TBD

### Phase 3: Shipment API
**Goal**: Shipments can be created, listed, and status-updated via REST, with events reliably published through the outbox pattern.
**Depends on**: Phase 2
**Requirements**: SHIP-01, SHIP-02, SHIP-03, SHIP-04
**Success Criteria** (what must be TRUE):
  1. POST /api/shipments creates a shipment and an outbox row in a single EF transaction
  2. GET /api/shipments returns all shipments; GET /api/shipments/{trackingNumber} returns a single shipment
  3. PUT /api/shipments/{id}/status changes status and records a ShipmentStatusChanged event in the outbox
  4. OutboxPublisherService (BackgroundService) polls pending outbox rows and publishes them to RabbitMQ with confirms
**Plans**: TBD

### Phase 4: Tracking API
**Goal**: Shipment events are consumed idempotently, tracked in a read model with Redis caching, and broadcast in real time via SignalR.
**Depends on**: Phase 3
**Requirements**: TRK-01, TRK-02, TRK-03, TRK-04, TRK-05
**Success Criteria** (what must be TRUE):
  1. Consumer processes events idempotently — a duplicate EventId is skipped (ProcessedEvents checked in same transaction)
  2. GET /api/tracking/{trackingNumber} returns tracking data with Redis cache-aside (10-min TTL, invalidated on event)
  3. SignalR hub broadcasts ShipmentUpdated to all connected operators and to the per-tracking-number group
  4. GET /api/admin/dlq returns dead-letter message count; POST /api/admin/dlq/replay replays DLQ messages to the main queue
**Plans**: TBD

### Phase 5: Web Frontends
**Goal**: Customers can create and track shipments through the MVC portal; operators can manage shipments with live updates through the Angular screen.
**Depends on**: Phase 4
**Requirements**: CUST-01, CUST-02, CUST-03, OPS-01, OPS-02, OPS-03, OPS-04
**Success Criteria** (what must be TRUE):
  1. Customer can create a shipment via the portal form (tracking number, description, destination)
  2. Customer can search by tracking number and see current status with full event timeline
  3. Operator sees a table of all shipments with live status badges
  4. Operator can update a shipment's status through the lifecycle (Created → PickedUp → InTransit → AtHub → OutForDelivery → Delivered / Exception)
  5. Operator receives live SignalR status updates without page refresh; timeline detail drawer shows full event history
**Plans**: TBD
**UI hint**: yes

### Phase 6: Docker, Tests & Documentation
**Goal**: The complete system runs as a single Docker Compose stack, core logic is tested, and the system is documented for end-to-end use.
**Depends on**: Phase 5
**Requirements**: PLAT-01, PLAT-02, PLAT-03, PLAT-04, PLAT-05
**Success Criteria** (what must be TRUE):
  1. `docker compose up --build` starts all services (Postgres 17, RabbitMQ 4, Redis 7, shipment-api, tracking-api, portal-web, operator-ui) with healthchecks passing
  2. Postgres init script creates both `shipments` and `tracking` databases
  3. `dotnet test` passes all xUnit tests covering consumer retry decisions, idempotency, and cache invalidation
  4. README documents how to build, run, and demo the system end-to-end
  5. End-to-end flow verified: create shipment in portal → update status in operator UI → portal timeline updates live
**Plans**: TBD

## Progress

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Foundation & Scaffold | 0/3 | Not started | - |
| 2. Contracts & Messaging | 0/? | Not started | - |
| 3. Shipment API | 0/? | Not started | - |
| 4. Tracking API | 0/? | Not started | - |
| 5. Web Frontends | 0/? | Not started | - |
| 6. Docker, Tests & Documentation | 0/? | Not started | - |
