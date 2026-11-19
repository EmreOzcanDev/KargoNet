# Phase 2: Contracts & Messaging - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-11-20
**Phase:** 2-Contracts & Messaging
**Areas discussed:** Event Contracts, RabbitMQ Topology, Publisher, Consumer Base, Testability

**Mode:** Autonomous smart discuss — full-autonomy run (no prompts). Hard-to-reverse questions took the recommended default; reversible technical questions were decided autonomously. No preference questions arose (infrastructure domain).

---

## Event Contracts

| Option | Description | Selected |
|--------|-------------|----------|
| Envelope record + typed payloads | `EventEnvelope { EventId, EventType, OccurredAt, Payload }`, JSON, camelCase | ✓ |
| Flat messages per event type | Each event type a distinct wire shape with no shared envelope | |
| ProtoBuf/other serializer | Binary wire format | |

**Choice (recommended default, one-way):** envelope record + typed payloads — cross-service contract persisted in the DLQ; System.Text.Json avoids a new dependency.

## RabbitMQ Topology

| Option | Description | Selected |
|--------|-------------|----------|
| kargonet.shipments topic exchange + main/retry/dlq | retry queue 5s TTL dead-letters back to main; DLQ on shipment.dead | ✓ |
| Direct exchange, single queue + in-memory retry | No TTL queue; retry via in-process timer | |

**Choice:** topic exchange layout — matches ROADMAP success criteria (retry queue with 5s TTL, DLQ with dead-letter routing).

## Publisher

| Option | Description | Selected |
|--------|-------------|----------|
| Await PublishAsync result per message | Publisher confirms, one channel, simple reconnect | ✓ |
| Batch confirm / async ack collection | Lower latency under load | |

**Choice (autonomous):** per-message awaited confirms — v1 volume is tiny; simplicity wins.

## Consumer Base

| Option | Description | Selected |
|--------|-------------|----------|
| Republish-to-retry-queue then ack original | x-retry-count incremented per failure; >= 3 → DLQ with x-exception | ✓ |
| nack-with-requeue loop | Broker requeues; retry count not trackable in headers | |

**Choice:** republish pattern — required by MSG-02/MSG-03 (hand-built retry via x-retry-count).

## Testability

| Option | Description | Selected |
|--------|-------------|----------|
| Pure decision function + interface seam, integration tests in Phase 6 | Unit-test retry/DLQ decisions without a broker | ✓ |
| Stand up a local broker now | Docker required (not installed; root-gated) | |

**Choice (autonomous):** testable pure logic now; live-broker integration tests deferred to Phase 6 Docker Compose.

## Decided autonomously

[auto] [Event Contracts] — Q: "How are events represented in C#?" → Selected: "records with an event envelope wrapper" (reversible technical — decided autonomously: local refactor while no consumer exists)
[auto] [Event Contracts] — Q: "Which serializer?" → Selected: "System.Text.Json, camelCase" (reversible technical — decided autonomously: local setting change while no cross-service traffic exists)
[auto] [RabbitMQ Topology] — Q: "Exchange/queue names and binding keys?" → Selected: "kargonet.shipments / shipments.main / shipments.retry / shipments.dlq" (reversible technical — decided autonomously: internal names, no other service references them yet)
[auto] [RabbitMQ Topology] — Q: "Does the retry queue dead-letter back to main?" → Selected: "yes — 5s TTL then back to shipments.main" (reversible technical — decided autonomously: queue argument change is local while no messages exist)
[auto] [Publisher] — Q: "How are confirms handled?" → Selected: "await PublishAsync result per message" (reversible technical — decided autonomously: internal swap while no producer service exists)
[auto] [Consumer Base] — Q: "Ack policy on failure?" → Selected: "republish then ack original; never requeue" (reversible technical — decided autonomously: local behavior change while no deployed consumers exist)
[auto] [Consumer Base] — Q: "Prefetch count?" → Selected: "1" (reversible technical — decided autonomously: config value, trivially changeable)
[auto] [Testability] — Q: "How to test without a local broker?" → Selected: "pure decision function + interface seam; integration tests in Phase 6" (reversible technical — decided autonomously: adding/moving the seam is a local refactor)

## Claude's Discretion

None — all areas resolved.

## Deferred Ideas

- Live-broker integration tests (require RabbitMQ via Docker — Phase 6)
- Broker connection resilience policy beyond simple reconnect
