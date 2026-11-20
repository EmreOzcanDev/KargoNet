# Requirements: KargoNet

**Defined:** 2026-11-19
**Core Value:** Reliable shipment status propagation: a status change made in the operator UI must reach the customer portal and the operator's live view without loss, even when RabbitMQ or individual consumers are temporarily down.

## v1 Requirements

### Customer Portal

- [ ] **CUST-01**: Customer can create a shipment with tracking number, description, and destination
- [ ] **CUST-02**: Customer can look up a shipment by tracking number
- [ ] **CUST-03**: Customer can see the shipment's current status and full event timeline

### Operator Screen

- [ ] **OPS-01**: Operator can see a table of all shipments with current status badges
- [ ] **OPS-02**: Operator can update a shipment's status through the lifecycle (Created, PickedUp, InTransit, AtHub, OutForDelivery, Delivered, Exception)
- [ ] **OPS-03**: Operator receives live status updates via SignalR without refreshing
- [ ] **OPS-04**: Operator can view a shipment's event timeline in a detail drawer

### Shipment API

- [x] **SHIP-01**: `POST /api/shipments` creates a shipment and records the `ShipmentCreated` event via outbox
- [x] **SHIP-02**: `GET /api/shipments` lists all shipments
- [x] **SHIP-03**: `GET /api/shipments/{trackingNumber}` returns a single shipment by tracking number
- [x] **SHIP-04**: `PUT /api/shipments/{id}/status` changes status and records `ShipmentStatusChanged` via outbox

### Tracking API

- [ ] **TRK-01**: Consumer processes shipment events with idempotency (ProcessedEvents checked by EventId in the same transaction)
- [ ] **TRK-02**: `GET /api/tracking/{trackingNumber}` returns tracking data with Redis cache-aside (10-min TTL, invalidated on event)
- [ ] **TRK-03**: SignalR hub broadcasts `ShipmentUpdated` to all connected operators and to a per-tracking-number group
- [ ] **TRK-04**: `GET /api/admin/dlq` returns dead-letter message count
- [ ] **TRK-05**: `POST /api/admin/dlq/replay` replays DLQ messages back to the main queue

### Messaging Infrastructure

- [x] **MSG-01**: RabbitMQ topology declares topic exchange, main queue, retry queue (5s TTL), and DLQ with proper dead-letter routing
- [x] **MSG-02**: Consumer base implements hand-built retry (max 3 attempts) using `x-retry-count` header
- [x] **MSG-03**: Messages exceeding retry limit are routed to DLQ with `x-exception` header
- [x] **MSG-04**: Publisher uses publisher confirms for reliable delivery

### Platform

- [ ] **PLAT-01**: Docker Compose stack with Postgres 17, RabbitMQ 4 (management UI on 15672), Redis 7, each with healthchecks
- [ ] **PLAT-02**: Multi-stage Dockerfiles for each service (shipment-api, tracking-api, portal-web, operator-ui)
- [ ] **PLAT-03**: Postgres init script creates `shipments` and `tracking` databases
- [ ] **PLAT-04**: xUnit tests cover consumer retry decisions, idempotency, and cache invalidation
- [ ] **PLAT-05**: README documents how to build, run, and demo the system end-to-end

## v2 Requirements

(None identified — all planned features are v1)

## Out of Scope

| Feature | Reason |
|---------|--------|
| Authentication / authorization | Plan explicitly excludes auth; customers track by number, operator screen is open |
| MassTransit or messaging abstractions | Hand-built retry/DLQ per plan — no middleware layer |
| Mobile apps | Web-only |
| Multi-tenant / multi-region deployment | Single Docker Compose stack |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| CUST-01 | Phase 5 | Pending |
| CUST-02 | Phase 5 | Pending |
| CUST-03 | Phase 5 | Pending |
| OPS-01 | Phase 5 | Pending |
| OPS-02 | Phase 5 | Pending |
| OPS-03 | Phase 5 | Pending |
| OPS-04 | Phase 5 | Pending |
| SHIP-01 | Phase 3 | Complete |
| SHIP-02 | Phase 3 | Complete |
| SHIP-03 | Phase 3 | Complete |
| SHIP-04 | Phase 3 | Complete |
| TRK-01 | Phase 4 | Pending |
| TRK-02 | Phase 4 | Pending |
| TRK-03 | Phase 4 | Pending |
| TRK-04 | Phase 4 | Pending |
| TRK-05 | Phase 4 | Pending |
| MSG-01 | Phase 2 | Complete |
| MSG-02 | Phase 2 | Complete |
| MSG-03 | Phase 2 | Complete |
| MSG-04 | Phase 2 | Complete |
| PLAT-01 | Phase 6 | Pending |
| PLAT-02 | Phase 6 | Pending |
| PLAT-03 | Phase 6 | Pending |
| PLAT-04 | Phase 6 | Pending |
| PLAT-05 | Phase 6 | Pending |

**Coverage:**
- v1 requirements: 25 total
- Mapped to phases: 25
- Unmapped: 0 ✓

---
*Requirements defined: 2026-11-19*
*Last updated: 2026-11-19 after roadmap creation*
