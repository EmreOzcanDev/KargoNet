# KargoNet — Shipment Tracking Microservices

KargoNet is a shipment-tracking system built on .NET 10 microservices. A **Shipment** service and a **Tracking** service exchange shipment events over **RabbitMQ** with custom **retry and dead-letter queues**, **PostgreSQL** for storage, a **Redis** cache for tracking lookups, an **ASP.NET Core MVC** customer portal, and an **Angular** operator screen that receives live updates over **SignalR**. The complete system runs as a single Docker Compose stack.

The core design goal is that a status change made in the operator UI reaches the customer portal and the operator's live view **without loss**, even when RabbitMQ or an individual consumer is temporarily unavailable.

## Architecture

```mermaid
flowchart LR
    Portal["Portal.Web (MVC)"] -->|"create / track"| ShipApi[Shipment.Api]
    Portal -->|"track lookup"| TrackApi[Tracking.Api]
    Operator["operator-ui (Angular)"] -->|"REST: list, update status"| ShipApi
    Operator -->|"SignalR /hubs/tracking"| TrackApi
    ShipApi --> ShipDb[("Postgres: shipments")]
    ShipApi -->|"outbox publisher"| Exchange{{"kargonet.shipments (topic)"}}
    Exchange --> MainQ[shipments.main]
    MainQ --> TrackApi
    TrackApi -->|"fail, attempt below max"| RetryQ["shipments.retry (5s TTL)"]
    RetryQ -->|"dead-letter back"| MainQ
    TrackApi -->|"fail, attempts exhausted"| Dlq[shipments.dlq]
    TrackApi --> TrackDb[("Postgres: tracking")]
    TrackApi --> Redis[("Redis cache")]
```

### Reliability and data flow

- **Outbox:** a status change and its event row are saved in one EF transaction; a background publisher polls, publishes with confirms, and marks rows sent — events are not lost if RabbitMQ is down.
- **Retry and DLQ:** on failure the consumer republishes to the retry queue (5-second TTL) with `x-retry-count` incremented; once its 2 retries (3 attempts in total) are exhausted the message goes to the DLQ with an `x-exception` header. Either way the original message is acked.
- **Idempotency:** each event is applied at most once — a `ProcessedEvents` marker is checked by `EventId` inside the same transaction as the read-model update.
- **Redis cache-aside:** `GET /api/tracking/{number}` serves from `tracking:{number}` (10-minute TTL); the entry is deleted when a new event for that shipment is consumed.
- **SignalR:** after each processed event, the Tracking API broadcasts `ShipmentUpdated` to all operator clients and to a group named after the tracking number.

## Prerequisites

- Docker with Docker Compose

## Quick start

```bash
docker compose up --build
```

The stack consists of 7 services, started in health-gated order. Once all report `healthy`, they are available as follows:

| Service | Host port | Description |
|---|---|---|
| portal-web | 5000 | Customer portal (ASP.NET Core MVC) |
| operator-ui | 4200 | Operator screen (Angular + nginx; proxies `/api` → shipment-api, `/hubs` → tracking-api) |
| shipment-api | 5001 | Shipment REST API |
| tracking-api | 5002 | Tracking REST API + SignalR hub + DLQ admin |
| postgres | 5432 | `shipments` and `tracking` databases |
| rabbitmq | 5672 / 15672 | Broker / management UI |
| redis | 6379 | Tracking cache |

To stop the stack, run `docker compose down`. Adding `-v` also removes the Postgres volumes; the databases are re-created on the next start.

Databases, tables, and RabbitMQ topology are created automatically on first start through an init script and in-process EF migrations at API startup.

## Demo walkthrough

1. **Create a shipment.** Open the customer portal at <http://localhost:5000> and submit the create-shipment form (tracking number, description, destination). The portal sends a POST request to the Shipment API, which returns `201 Created`.
2. **View it in the operator screen.** Open <http://localhost:4200>. The new shipment appears in the table with a live badge.
3. **Advance the status.** Change the status in the operator screen (PickedUp → InTransit → …). This calls `PUT /api/shipments/{id}/status` on the Shipment API.
4. **Observe the live update.** The status badge updates in real time over SignalR, without a page refresh.
5. **Check the portal timeline.** The tracking view (`/track?number=…`) shows the current status and a timeline entry for each status change.
6. **Exercise the dead-letter queue.** Publish a poison message to the `shipments.main` queue, for example through the RabbitMQ management UI at <http://localhost:15672>. `x-retry-count` increments as the message cycles through `shipments.retry`, after which it lands in `shipments.dlq`. Replay it with:

   ```bash
   curl -X POST http://localhost:5002/api/admin/dlq/replay
   ```

   The DLQ count drops as the message is reprocessed.

## Development mode

Docker Compose is the supported development flow and the only place where connection strings are preconfigured. The root `.env` file is consumed by Compose; ASP.NET Core does not load it, and the `appsettings*.json` files contain no connection strings.

Running services directly on the host requires local instances of PostgreSQL (databases `shipments` and `tracking`), RabbitMQ, and Redis. Set the following environment variables before `dotnet run`:

| Variable | Used by | Example (localhost) |
|---|---|---|
| `ConnectionStrings__ShipmentDb` | Shipment API | `Host=localhost;Database=shipments;Username=…;Password=…` |
| `ConnectionStrings__TrackingDb` | Tracking API | `Host=localhost;Database=tracking;Username=…;Password=…` |
| `ConnectionStrings__Redis` | Tracking API | `localhost:6379` |
| `ConnectionStrings__RabbitMq` | both APIs | `amqp://…@localhost:5672/` |

Start each component:

- **APIs:** `dotnet run` in `src/Services/Shipment.Api` (port 5001) and `src/Services/Tracking.Api` (port 5002). Migrations run in-process at startup.
- **Portal:** `dotnet run` in `src/Web/Portal.Web` (port 5000).
- **Operator UI:** `npm install && npm run start` in `src/Web/operator-ui` (`ng serve`, port 4200). The development `environment.ts` uses absolute `localhost:5001`/`localhost:5002` bases.

## Project layout

```
src/BuildingBlocks/KargoNet.Contracts   Event records, ShipmentStatus enum, envelope
src/BuildingBlocks/KargoNet.Messaging   Rabbit topology, publisher, consumer base with retry/DLQ
src/Services/Shipment.Api               Shipment REST API, outbox, background publisher
src/Services/Tracking.Api               Event consumer, read model, Redis cache, SignalR hub, DLQ admin
src/Web/Portal.Web                      Customer portal (MVC)
src/Web/operator-ui                     Angular operator screen + nginx proxy config
tests/KargoNet.Tracking.Tests           xUnit: retry decisions, idempotency, cache, consumer, API
tests/KargoNet.Portal.Tests             xUnit: portal controllers
docker/postgres/init.sql                Creates the shipments and tracking databases
docker-compose.yml                      The 7-service stack
.env                                    Default credentials and connection strings for local development
```

## Tests

```bash
dotnet test              # full xUnit suite (repo root)
```

and in `src/Web/operator-ui`:

```bash
npm test                 # Angular test suite (ng test, vitest-backed)
```

The suite covers the three core reliability areas:

- **Retry decisions** — `RetryPolicyTests` (republish at counts 1 and 2, route to DLQ at the cap) and `ShipmentEventsConsumerTests` (`Handler_failure_routes_to_retry_then_dlq_and_always_ackd`, `Unknown_event_type_routes_to_retry_then_dlq_and_always_ackd`, `DbUpdateException_without_committed_marker_is_rethrown_and_routed_to_retry`).
- **Idempotency** — `ShipmentEventsConsumerTests` (`Redelivered_same_event_id_is_applied_exactly_once_and_ackd`, `Race_backstop_committed_marker_is_ackd_as_success_without_retry`, `Race_backstop_duplicate_processed_marker_returns_success_without_rethrowing`).
- **Cache invalidation** — `ShipmentEventsConsumerTests.Status_changed_body_updates_status_and_appends_timeline_row` (asserts the cache entry is deleted on every applied event) plus the cache-aside endpoint tests in `TrackingEndpointTests` (`Get_known_tracking_number_returns_200_dto_and_warms_the_cache`, `Second_get_is_served_from_cache_not_the_database`, `Get_degrades_to_EF_when_the_cache_fails`).

## DLQ admin endpoints (tracking-api)

- `GET /api/admin/dlq` — count of messages currently in the dead-letter queue
- `POST /api/admin/dlq/replay` — requeue all DLQ messages back to the main queue and report how many were replayed
