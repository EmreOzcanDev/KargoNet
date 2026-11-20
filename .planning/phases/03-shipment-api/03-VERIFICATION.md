---
phase: 03-shipment-api
verified: 2026-11-20T01:05:24Z
status: passed
score: 4/4
behavior_unverified: 0
overrides_applied: 0
covered_files:
  - ".planning/phases/03-shipment-api/03-01-PLAN.md"
  - ".planning/phases/03-shipment-api/03-01-SUMMARY.md"
  - ".planning/phases/03-shipment-api/03-02-PLAN.md"
  - ".planning/phases/03-shipment-api/03-02-SUMMARY.md"
  - ".planning/phases/03-shipment-api/03-03-PLAN.md"
  - ".planning/phases/03-shipment-api/03-03-SUMMARY.md"
  - ".planning/phases/03-shipment-api/03-REVIEW.md"
  - "src/BuildingBlocks/KargoNet.Messaging/Publishing/LazyOutboxChannelFactory.cs"
  - "src/Services/Shipment.Api/Data/ShipmentDbContext.cs"
  - "src/Services/Shipment.Api/Data/ShipmentDbContextFactory.cs"
  - "src/Services/Shipment.Api/Dto/ShipmentDto.cs"
  - "src/Services/Shipment.Api/Endpoints/Shipments.cs"
  - "src/Services/Shipment.Api/Entities/OutboxMessage.cs"
  - "src/Services/Shipment.Api/Entities/Shipment.cs"
  - "src/Services/Shipment.Api/Migrations/20261119232650_InitialCreate.cs"
  - "src/Services/Shipment.Api/Program.cs"
  - "src/Services/Shipment.Api/Publishing/OutboxPublisherService.cs"
  - "tests/KargoNet.Tracking.Tests/Api/ShipmentEndpointTests.cs"
  - "tests/KargoNet.Tracking.Tests/Data/ShipmentDbContextTests.cs"
  - "tests/KargoNet.Tracking.Tests/Publishing/OutboxPublisherServiceTests.cs"
covered_digest: "v4:sha256:8521525b2d3fd59501b872d1c3cb3b2315c8939ff8b4cb3c1713c159278a0de3"
---

# Phase 3: Shipment API — Verification Report

**Phase Goal:** Shipments can be created, listed, and status-updated via REST, with events reliably published through the outbox pattern.
**Verified:** 2026-11-20T01:05:24Z
**Status:** passed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths (Success Criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| SC1 | POST /api/shipments creates a shipment and an outbox row in a single EF transaction | ✓ VERIFIED | `CreateShipment` (Shipments.cs:36-96) wraps `db.Shipments.Add(shipment)` + `db.OutboxMessages.Add(outbox)` in `BeginTransactionAsync`/`SaveChangesAsync`/`CommitAsync` (Shipments.cs:80-87) and returns 201 + `ShipmentDto`. Duplicate tracking number → 409 via pre-check (Shipments.cs:50-52) plus a `DbUpdateException`/SqlState `23505` backstop for the real Npgsql unique index (Shipments.cs:88-93). Over-long input (tracking >64 / destination >256) → 400, nothing persisted (Shipments.cs:44-45). Behavioral test `Create_shipment_persists_shipment_and_outbox_row_atomically` (ShipmentEndpointTests.cs:64-95) asserts both rows landed and that the stored `PayloadJson` round-trips through Phase 2 `EventSerializer` with the correct trackingNumber/description/destination. Supporting tests: duplicate→409, empty/whitespace→400 (0 rows), over-long→400 (0 rows) — all pass. |
| SC2 | GET /api/shipments returns all shipments; GET /api/shipments/{trackingNumber} returns a single shipment | ✓ VERIFIED | `ListShipments` (Shipments.cs:98-102) returns all rows ordered by `CreatedAt`; `GetShipmentByTrackingNumber` (Shipments.cs:104-113) returns 200 + `ShipmentDto` or 404. Tests `List_shipments_returns_shipments_ordered_by_created_at` (ShipmentEndpointTests.cs:149-163, asserts exact order of 3 created shipments) and `Get_by_tracking_number_returns_200_when_present_and_404_when_absent` (ShipmentEndpointTests.cs:166-179) pass. |
| SC3 | PUT /api/shipments/{id}/status changes status and records a ShipmentStatusChanged event in the outbox | ✓ VERIFIED | `UpdateShipmentStatus` (Shipments.cs:115-152) parses the status name (400 on unknown), 404 when absent, then updates `Status`/`UpdatedAt` and inserts a `shipment.status.changed` `OutboxMessage` in one `BeginTransactionAsync`/`CommitAsync` (Shipments.cs:146-149), returning 200 + updated `ShipmentDto`. Behavioral test `Update_status_persists_status_and_status_changed_outbox_row` (ShipmentEndpointTests.cs:182-214) asserts the persisted status, the single outbox row, and that the payload carries `shipmentId`, `trackingNumber`, `fromStatus=Created`, `toStatus=InTransit` via `EventSerializer.Deserialize`. `Update_status_with_missing_shipment_returns_404` (ShipmentEndpointTests.cs:217-224) passes. |
| SC4 | OutboxPublisherService (BackgroundService) polls pending outbox rows and publishes them to RabbitMQ with confirms | ✓ VERIFIED (live-ack deferred to Phase 6) | `OutboxPublisherService : BackgroundService` (OutboxPublisherService.cs:21-114): `PollOnceAsync` (44-90) selects `Pending` rows `OrderBy(CreatedAt).Take(50)`, publishes each row's stored `PayloadJson` **verbatim** to `RabbitTopology.ShipmentRoutingKey` via `MessagePublisher`, and on confirm marks the row `Published` + `PublishedAt` in its own short transaction (73-75); a failure leaves the row `Pending`, never deletes, and deliberately avoids `ChangeTracker.Clear()` so remaining batch rows stay attached (79-86). `ExecuteAsync` (92-114) loops on a 2 s interval and catches non-cancellation poll failures so a DB outage does not kill the host. DI in Program.cs: `IOutboxChannel → LazyOutboxChannelFactory` (channel opened lazily on first publish with `CreateChannelOptions(true, false)` = publisher confirms on; no connection at startup), `MessagePublisher`, `AddHostedService<OutboxPublisherService>`. Six behavioral tests pass: verbatim publish to `kargonet.shipments`/`shipment` + confirm-mark; unconfirmed → Pending + retried next poll; Published row skipped; batch capped at 50 oldest-first; failed first row does not detach remaining rows (CR-01 regression); poll failure does not terminate the host loop. **The live-broker confirm round-trip (real broker ack) is a documented deferral to Phase 6** (Docker unavailable) — acceptable because the confirm-decision logic was implemented and unit-tested in Phase 2 and the `RabbitOutboxChannel` adapter is in place as a drop-in. |

**Score:** 4/4 truths verified (0 present-behavior-unverified)

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `Entities/Shipment.cs` | Shipment entity | ✓ VERIFIED | Id, TrackingNumber, Description?, Destination, Status (enum), CreatedAt, UpdatedAt — Shipment.cs:5-20 |
| `Entities/OutboxMessage.cs` | OutboxMessage entity + OutboxStatus enum | ✓ VERIFIED | Id, EventType, PayloadJson, Status, CreatedAt, PublishedAt?; `OutboxStatus { Pending, Published }` — OutboxMessage.cs:3-21 |
| `Data/ShipmentDbContext.cs` | DbContext with both sets + indexes | ✓ VERIFIED | `TrackingNumber` HasMaxLength(64) + unique index; `Status` string-backed; outbox index on `(Status, CreatedAt)` — ShipmentDbContext.cs:19-35 |
| `Data/ShipmentDbContextFactory.cs` | Design-time factory for `dotnet ef` | ✓ VERIFIED | `IDesignTimeDbContextFactory` present (plan 03-01 artifact) |
| `Migrations/20261119232650_InitialCreate.cs` | InitialCreate migration (Npgsql) for both tables | ✓ VERIFIED | Migration + Designer + `ShipmentDbContextModelSnapshot.cs` present under `src/Services/Shipment.Api/Migrations/` (directory artifact — files exist) |
| `Endpoints/Shipments.cs` | Four minimal-API endpoints with atomic outbox writes | ✓ VERIFIED | POST/GET list/GET by tracking/PUT status, both write paths in single EF transactions — Shipments.cs:36-152 |
| `Dto/ShipmentDto.cs` | ShipmentDto + request records | ✓ VERIFIED | `ShipmentDto` (id, trackingNumber, description, destination, status, createdAt, updatedAt) + `CreateShipmentRequest`/`UpdateStatusRequest` — wired by endpoint tests deserializing responses |
| `Publishing/OutboxPublisherService.cs` | BackgroundService poll loop + drivable poll-batch method | ✓ VERIFIED | `PollOnceAsync` public/directly drivable; 2 s loop in `ExecuteAsync` — OutboxPublisherService.cs:21-114 |
| `Messaging/Publishing/LazyOutboxChannelFactory.cs` | Lazy `IOutboxChannel` with confirms | ✓ VERIFIED | Guarded lazy open, `CreateChannelOptions(true, false)` (confirms on), nothing connects at startup — wired in Program.cs |

All 9 artifacts exist, are substantive, and are wired (referenced by Program.cs, the endpoints, and the passing tests).

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `Endpoints/Shipments.cs` | `Data/ShipmentDbContext.cs` | each write path wraps entity insert/update + OutboxMessage insert in one `BeginTransaction`/`Commit` | WIRED | Shipments.cs:82-87 (POST) and 146-149 (PUT); gsd-tools `verify.key-links` 03-02: pattern found |
| `Publishing/OutboxPublisherService.cs` | `Messaging/Publishing/MessagePublisher.cs` | per-row `PublishAsync(RabbitTopology.ShipmentRoutingKey, PayloadJson bytes)` then confirm-mark in its own transaction | WIRED | OutboxPublisherService.cs:62-75; gsd-tools `verify.key-links` 03-03: pattern found |
| `Program.cs` | publisher stack | `IOutboxChannel → LazyOutboxChannelFactory`, `MessagePublisher`, `AddHostedService<OutboxPublisherService>`, `ShipmentDbContext` (Npgsql / InMemory switch) | WIRED | Program.cs registrations; endpoint tests boot the real host via `WebApplicationFactory<Program>` and pass |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Solution builds clean | `~/.dotnet/dotnet build KargoNet.sln` | `Build succeeded. 0 Warning(s) 0 Error(s)` | ✓ PASS |
| Full test suite | `~/.dotnet/dotnet test KargoNet.sln` | `Passed! - Failed: 0, Passed: 42, Skipped: 0, Total: 42` (26 Phase 2 + 16 Phase 3) | ✓ PASS |
| Atomic outbox writes | (within 42-test run) `ShipmentEndpointTests` | 8/8 pass, incl. atomic create+outbox, 409/400 paths, atomic status change + outbox | ✓ PASS |
| Outbox publish state transitions | (within 42-test run) `OutboxPublisherServiceTests` | 6/6 pass, incl. unconfirmed→Pending retry, batch-50 oldest-first, no batch detach on failure, host loop survives poll failure | ✓ PASS |

Behavior-dependent truths (atomic write-then-commit, per-row confirm-mark, leave-Pending-on-failure, batch cap/ordering, host-survives-poll-failure, batch-not-detached-by-one-failure) are exercised by passing behavioral tests — not merely present. No truth is left ⚠️ PRESENT_BEHAVIOR_UNVERIFIED.

### Anti-Patterns Found

Scanned all 12 Phase 3 source/test files for `TBD|FIXME|XXX|TODO|HACK|PLACEHOLDER|not implemented|coming soon` — **none found**. No skipped tests in the three Phase 3 test files (`Fact.Skip`/`Theory.Skip`/`Skip =` — none). No disabled tests on any requirement. No stubs: every endpoint returns real DB-derived data; no hardcoded empty returns.

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| SHIP-01 | 03-01/02 | POST /api/shipments creates a shipment and records the ShipmentCreated event via outbox | ✓ SATISFIED | `CreateShipment` atomic write + `Create_shipment_persists_shipment_and_outbox_row_atomically` |
| SHIP-02 | 03-02 | GET /api/shipments lists all shipments | ✓ SATISFIED | `ListShipments` + `List_shipments_returns_shipments_ordered_by_created_at` |
| SHIP-03 | 03-02 | GET /api/shipments/{trackingNumber} returns a single shipment | ✓ SATISFIED | `GetShipmentByTrackingNumber` + `Get_by_tracking_number_returns_200_when_present_and_404_when_absent` |
| SHIP-04 | 03-03 | PUT /api/shipments/{id}/status changes status and records ShipmentStatusChanged via outbox | ✓ SATISFIED | `UpdateShipmentStatus` + `Update_status_persists_status_and_status_changed_outbox_row` (+ publisher SC4 stack) |

No orphaned requirements: ROADMAP Phase 3 lists SHIP-01..04, all covered by plans 03-01..03.

### Test Quality Audit

| Test File | Linked Req | Active | Skipped | Circular | Assertion Level | Verdict |
|-----------|-----------|--------|---------|----------|-----------------|---------|
| `Api/ShipmentEndpointTests.cs` | SHIP-01/02/03 | 8 | 0 | No | Behavioral (real host via WebApplicationFactory; DB rows asserted; payload round-tripped through EventSerializer) | OK |
| `Publishing/OutboxPublisherServiceTests.cs` | SHIP-04 | 6 | 0 | No | Behavioral (exchange/routing key/body verbatim, status transitions, batch cap, failure isolation, host-loop survival) | OK |
| `Data/ShipmentDbContextTests.cs` | SHIP-01 | 2 | 0 | No | Value (model/index shape) | OK |

**Disabled tests on requirements:** 0. **Circular patterns:** 0. **Insufficient assertions:** 0.

### Human Verification

N/A — Infrastructure/API phase with no user-facing elements. All acceptance criteria are verifiable programmatically. The live-broker behavior (SC4 real-broker confirm round-trip) is a documented deferral to Phase 6, not a human-verification item for this phase.

### Deferred to Phase 6 (Not Gaps)

Live-Postgres/RabbitMQ behavior is explicitly deferred to Phase 6 (Docker Compose stack; Docker is not installed in this environment). These are documented, tracked deferrals (03-CONTEXT.md, 03-REVIEW.md IN-01) — **not gaps**:

| Deferred Item | Rationale | Phase 6 Evidence |
|---------------|-----------|------------------|
| Live Postgres e2e / migration applied to a real DB | Entities, DbContext, indexes, and InitialCreate migration are in place and model-tested with InMemory; the SqlState 23505 backstop is code-complete | Phase 6 SC1: `docker compose up` starts Postgres 17; API runs against it |
| Live broker confirm round-trip (SC4 real broker ack) | Confirm-decision logic implemented + unit-tested (Phase 2); `RabbitOutboxChannel` + `LazyOutboxChannelFactory` in place as drop-ins; publisher state transitions unit-tested end-to-end with a fake `IOutboxChannel` | Phase 6 stack brings up a real broker; live publish path exercised |

Per 03-CONTEXT.md ("Live-broker e2e (real publish over compose) remains a Phase 6 deferral") and 03-REVIEW.md IN-01, both deferrals are explicitly scoped to Phase 6 and are acceptable.

---

## Verification Summary

All four success criteria are satisfied by code that is present, substantive, wired, and behaviorally proven by a passing 42/42 test suite (16 new Phase 3 tests: 8 endpoint, 6 publisher, 2 DbContext). The single broker-only half of SC4 (live acknowledgment) is a documented deferral to Phase 6, with the confirm-decision logic implemented and unit-tested in Phase 2 and the lazy confirmed channel adapter in place. No gaps, no blockers, no anti-patterns, no skipped tests, no human-verification items.

**Status: passed (4/4 must-haves verified).**

---

_Verified: 2026-11-20T01:05:24Z_
_Verifier: Claude (gsd-verifier)_
