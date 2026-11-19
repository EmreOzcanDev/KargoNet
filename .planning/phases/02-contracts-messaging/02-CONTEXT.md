# Phase 2: Contracts & Messaging - Context

**Gathered:** 2026-11-20
**Status:** Ready for planning
**Mode:** Autonomous smart discuss (full-autonomy run — no prompts; hard-to-reverse items take the recommended default)

<domain>
## Phase Boundary

Shipment events can be published and consumed over RabbitMQ with reliable delivery, hand-built retry, and dead-letter routing. This phase delivers: event contract records in KargoNet.Contracts (ShipmentCreated, ShipmentStatusChanged, envelope), RabbitMQ topology (topic exchange, main queue, retry queue with 5s TTL, DLQ), a publisher with publisher confirms, and a consumer base that implements the hand-built retry (max 3 attempts via x-retry-count) and dead-letter routing (x-exception header) in KargoNet.Messaging. No REST endpoints, no EF Core, no consumers for specific events (Phases 3-4).

</domain>

<decisions>
## Implementation Decisions

### Event Contracts
- Envelope shape: `EventEnvelope { Guid EventId; string EventType; DateTimeOffset OccurredAt; JsonElement? Payload }` with typed payload records `ShipmentCreatedPayload { TrackingNumber, Description, Destination }` and `ShipmentStatusChangedPayload { ShipmentId, TrackingNumber, FromStatus, ToStatus }` — **Reversibility:** one-way — cross-service JSON contract: the payload is serialized into RabbitMQ and persisted in the DLQ, and Phases 3-4 will deserialize it; changing field names/shapes later means migrating in-flight and dead-lettered messages
- `ShipmentStatus` enum (seven lifecycle values) already lives in Contracts from Phase 1; event payload references it by name
- Serialization via System.Text.Json (camelCase); no third-party serialization dependency

### RabbitMQ Topology
- Topic exchange `kargonet.shipments`; main queue `shipments.main` bound with routing key `shipment`; retry queue `shipments.retry` bound with `shipment.retry`, args `x-message-ttl=5000`, `x-dead-letter-exchange=kargonet.shipments`, `x-dead-letter-routing-key=shipment`; DLQ `shipments.dlq` bound with `shipment.dead`
- Retry path: failing consumer republishes to the exchange with routing key `shipment.retry`; after 5s TTL the retry queue dead-letters back to `shipments.main`
- DLQ path: exhausted retry republishes with routing key `shipment.dead` and `x-exception` header, then acks the original

### Publisher
- RabbitMQ.Client 7.x async API: `IChannel.PublishAsync` with publisher confirms (await the `PublishResult`); single connection/channel per service, reconnected on failure
- Publishing helper exposes `Task PublishAsync(string routingKey, ReadOnlyMemory<byte> body, IDictionary<string, object?>? headers)`

### Consumer Base
- `ConsumerBase<T>` (or equivalent) wraps: consume from main queue, prefetch 1, ack on handler success; on handler failure: read `x-retry-count`, if attempts < 3 republish to retry queue with incremented `x-retry-count` and original headers + `x-exception`, ack original; if attempts >= 3 republish to DLQ with `x-exception` header, ack original
- "Attempts below 3" semantics: first delivery is attempt 1; republishes happen for attempts 1 and 2; a third failed attempt goes to the DLQ
- nack-without-requeue is never used for retry (republish is the retry mechanism); unhandled poison semantics do not exist because every failure path ends in ack

### Testability
- Retry/DLQ decision logic extracted as a pure function (attempt count + exception -> { RepublishToRetry, RouteToDlq }) unit-testable without a broker
- Broker interaction isolated behind a small interface so the consumer base is testable; live-broker integration tests deferred to Phase 6 (Docker Compose)

### Decided autonomously
- [Event Contracts] Q: "How are events represented in C#?" → Selected: "records with an event envelope wrapper" — rename/rearrange is a local refactor while no consumer exists
- [Event Contracts] Q: "Which serializer?" → Selected: "System.Text.Json, camelCase" — swap is a local setting change while no cross-service traffic exists
- [RabbitMQ Topology] Q: "Exchange/queue names and binding keys?" → Selected: "kargonet.shipments / shipments.main / shipments.retry / shipments.dlq" — internal names; renaming is cheap while no other service references them
- [RabbitMQ Topology] Q: "Does the retry queue dead-letter back to main?" → Selected: "yes — 5s TTL then back to shipments.main" — argument change is local while no messages exist
- [Publisher] Q: "How are confirms handled?" → Selected: "await PublishAsync result per message" — alternative (batch/async ack collection) is an internal swap
- [Consumer Base] Q: "Ack policy on failure?" → Selected: "republish then ack original; never requeue" — local behavior change while no deployed consumers exist
- [Consumer Base] Q: "Prefetch count?" → Selected: "1" — config value, trivially changeable
- [Testability] Q: "How to test without a local broker?" → Selected: "pure decision function + interface seam; integration tests in Phase 6" — adding/moving the seam is a local refactor

### Claude's Discretion
None — all areas resolved.

</decisions>

<code_context>
## Existing Code Insights

### Reusable Assets
- `KargoNet.Contracts` — `ShipmentStatus` enum (seven values) already exists from Phase 1
- `KargoNet.Messaging` — empty project, ready for this phase's code
- `KargoNet.Tracking.Tests` — xUnit test project (runner pinned to xunit.runner.visualstudio 2.8.2 / xunit 2.9.3 from Phase 1 review fix)

### Established Patterns
- Classic .sln (`KargoNet.sln`), Directory.Build.props for shared build settings
- .NET 10 SDK at `~/.dotnet/dotnet` (not on PATH)

### Integration Points
- Phase 3 will add `OutboxPublisherService` using the publisher helper from this phase
- Phase 4 will add the tracking consumer via `ConsumerBase` and the DLQ admin endpoints over the DLQ
- RabbitMQ.Client 7.x async API (connection factory, IChannel, PublishAsync, BasicConsumeAsync)

</code_context>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches. AGENTS.md pins: hand-built retry/DLQ, 3 retries, 5s TTL, RabbitMQ.Client 7.x async.

</specifics>

<deferred>
## Deferred Ideas

- Live-broker integration tests (require RabbitMQ via Docker — Phase 6)
- Connection resilience/retry policy for the broker connection itself (keep simple reconnect for v1)

</deferred>
