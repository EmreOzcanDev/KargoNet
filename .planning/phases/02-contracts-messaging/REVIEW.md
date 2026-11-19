---
phase: 02-contracts-messaging
reviewed: 2026-11-19T22:00:00Z
depth: standard
files_reviewed: 21
files_reviewed_list:
  - src/BuildingBlocks/KargoNet.Contracts/Events/EventEnvelope.cs
  - src/BuildingBlocks/KargoNet.Contracts/Events/EventSerializer.cs
  - src/BuildingBlocks/KargoNet.Contracts/Events/ShipmentCreatedPayload.cs
  - src/BuildingBlocks/KargoNet.Contracts/Events/ShipmentStatusChangedPayload.cs
  - src/BuildingBlocks/KargoNet.Messaging/KargoNet.Messaging.csproj
  - src/BuildingBlocks/KargoNet.Messaging/Topology/RabbitTopology.cs
  - src/BuildingBlocks/KargoNet.Messaging/Topology/TopologySpec.cs
  - src/BuildingBlocks/KargoNet.Messaging/Publishing/IOutboxChannel.cs
  - src/BuildingBlocks/KargoNet.Messaging/Publishing/MessagePublisher.cs
  - src/BuildingBlocks/KargoNet.Messaging/Publishing/PublishOutcome.cs
  - src/BuildingBlocks/KargoNet.Messaging/Publishing/RabbitOutboxChannel.cs
  - src/BuildingBlocks/KargoNet.Messaging/Consuming/ConsumerBase.cs
  - src/BuildingBlocks/KargoNet.Messaging/Consuming/IConsumerBroker.cs
  - src/BuildingBlocks/KargoNet.Messaging/Consuming/RetryDecision.cs
  - src/BuildingBlocks/KargoNet.Messaging/Consuming/RetryPolicy.cs
  - tests/KargoNet.Tracking.Tests/KargoNet.Tracking.Tests.csproj
  - tests/KargoNet.Tracking.Tests/Contracts/EventSerializerTests.cs
  - tests/KargoNet.Tracking.Tests/Messaging/ConsumerBaseTests.cs
  - tests/KargoNet.Tracking.Tests/Messaging/MessagePublisherTests.cs
  - tests/KargoNet.Tracking.Tests/Messaging/RetryPolicyTests.cs
  - tests/KargoNet.Tracking.Tests/Messaging/TopologyTests.cs
findings:
  critical: 2
  warning: 2
  info: 1
  total: 5
status: findings
---

# Phase 2: Contracts & Messaging — Code Review Report

**Reviewed:** 2026-11-19T22:00:00Z
**Depth:** standard
**Files Reviewed:** 21
**Status:** findings

## Summary

Reviewed all source and test files introduced by commits 67cf770, b944130, d4ff9ff, 1ba65d5, 9542083 against the locked spec in 02-CONTEXT.md. Spec compliance is strong overall: the retry decision boundary is correct (republish on failed attempts 1 and 2, DLQ on attempt 3 — verified `Decide(0)→1`, `Decide(1)→2`, `Decide(2)→DLQ`), the topology names/routing keys/TTL+dead-letter arguments match the spec exactly, the publisher signature matches, and ack-after-republish with no nacks is implemented. Tests assert real behavior (header values, ack ordering, wire format) with no tautologies.

Two Critical findings: a premature-confirm race in `RabbitOutboxChannel` under concurrent publishes (silent data loss), and deserialization sitting outside the consumer's failure path so a single malformed message can wedge the consumer in a permanent requeue loop — violating the spec's "every failure path ends in ack" invariant. Two warnings cover the same invariant family in the consumer (unparseable `x-retry-count` header; failed republish propagating without ack). Known accepted deviations (7.2.2 confirm API naming, `JsonDocument.Parse(byte[])` copy, DLQ path keeping original retry count) were not flagged.

## Critical Issues

### CR-01: `e.Multiple` short-circuit in the ack handler confirms publishes that have not been acked

**Resolution:** fixed in `1451807` — `e.Multiple ||` dropped; ack handler now confirms only on `e.DeliveryTag >= publishId`. Pinned by `RabbitOutboxChannelTests` (multi-ack of an earlier tag does not confirm a later in-flight publish; single ack of own tag confirms).
**File:** `src/BuildingBlocks/KargoNet.Messaging/Publishing/RabbitOutboxChannel.cs:39`
**Issue:** The ack handler fires `TrySetResult(true)` on `e.Multiple || e.DeliveryTag >= publishId`. A multiple-ack frame covers all tags **up to** `e.DeliveryTag`; it does not imply the current publish's tag is covered. With concurrent publishes on the shared channel (the designed usage: one channel per service, outbox publishes in Phase 3 plus consumer republishes in Phase 4 all through the same `MessagePublisher`/`IChannel`), this is reachable: publishes tag N-2 and N-1 are in flight when tag N publishes; the broker acks N-2 and N-1 as a **multiple** ack with `DeliveryTag == N-1 < N`. The handler for tag N sees `e.Multiple == true`, marks its own publish confirmed, and `PublishOutcome.IsConfirmed` becomes true before the broker has acked tag N. If the channel then closes (broker blip, connection reset) before tag N is actually acked, the message is lost while the caller believes it was delivered — silent data loss, which is exactly what the confirm mechanism exists to prevent. The `||` clause is also redundant: a multiple ack with `DeliveryTag >= publishId` covers the tag, and a single ack with `DeliveryTag >= publishId` means the broker (which acks in order) has already acked our tag, so the tag comparison alone is necessary and sufficient.
**Fix:**
```csharp
AsyncEventHandler<BasicAckEventArgs> onAcks = (object sender, BasicAckEventArgs e) =>
{
    // Both single and multiple acks cover all tags up to e.DeliveryTag;
    // a frame only confirms this publish when its tag is >= ours.
    if (e.DeliveryTag >= (ulong)publishId)
        acked.TrySetResult(true);
    return Task.CompletedTask;
};
```

### CR-02: Deserialization is outside the failure path — a malformed body can never be acked

**Resolution:** fixed in `59a268c` — deserialization moved inside the try block; decode failures (`envelope is null`) force `RouteToDlq` with the raw corrupt body preserved. Pinned by `ConsumerBaseTests.MalformedBody_RoutesToDlqAndAcks`.
**File:** `src/BuildingBlocks/KargoNet.Messaging/Consuming/ConsumerBase.cs:30`
**Issue:** `EventSerializer.Deserialize<TPayload>(body.Span)` runs before the `try` block. Any deserialization failure (`JsonException`, `KeyNotFoundException` for a missing `eventId`/`payload`, `ArgumentException` for a wrong value type, or a `null`-forgiven `eventType`) propagates out of `HandleDeliveryAsync` without calling `AckAsync`. The broker then requeues the message after the consumer channel dies, the handler fails again identically, and with prefetch 1 the queue is wedged forever on that one message. This directly contradicts the locked spec: "unhandled poison semantics do not exist because every failure path ends in ack." A non-envelope message (manual publish, out-of-band tooling, a future producer bug) is enough to trigger it.
**Fix:** Move deserialization inside the failure path and route decode failures to the DLQ (they will never succeed on retry):
```csharp
EventEnvelope<TPayload>? envelope = null;
try
{
    envelope = EventSerializer.Deserialize<TPayload>(body.Span);
    await _handler(envelope).ConfigureAwait(false);
}
catch (Exception exception)
{
    // existing header-copy + RetryPolicy.Decide logic; for decode failures
    // (envelope is null) force RouteToDlq, because a corrupt body will
    // never deserialize successfully on retry.
}
```
Note the DLQ body is then the raw corrupted bytes — which is the desired outcome for a dead-lettered poison message. Add a `ConsumerBaseTests` case for a malformed body (currently untested).

## Warnings

### WR-01: `Convert.ToInt32(raw)` on an untrusted `x-retry-count` header can throw and skip the ack

**Resolution:** fixed in `9e5e78f` — `Convert.ToInt32` replaced by `ParseRetryCount`, which accepts `int` and in-range `long` and treats anything else as the first attempt (0). Pinned by `ConsumerBaseTests.UnparseableRetryCountHeader_TreatedAsFirstAttempt`.
**File:** `src/BuildingBlocks/KargoNet.Messaging/Consuming/ConsumerBase.cs:38-40`
**Issue:** `x-retry-count` is read from delivery headers and converted with `Convert.ToInt32(raw)`. If the value is a non-numeric string, an out-of-range number, or an unexpected AMQP type, `FormatException`/`OverflowException`/`InvalidCastException` is thrown **inside the catch block**, so `AckAsync` is again never reached — same permanent-wedge outcome as CR-02, and equally a violation of "every failure path ends in ack." The header value is broker-visible input, not something this code controls (an operator can set it on a redelivered message; a buggy future producer could too).
**Fix:**
```csharp
int retryCount = 0;
if (headers is not null
    && headers.TryGetValue(RetryCountHeader, out var raw)
    && raw is int i)
{
    retryCount = i;
}
// treat unparseable/missing values as 0 → republish path re-issues a clean
// int header; a corrupt counter gets at most the normal retry budget.
```

### WR-02: A failed republish propagates without acking the original — broker requeues, violating "never requeue"

**Resolution:** fixed in `135ddd4` — republish wrapped in try/catch; the original delivery is acked on every path, preserving the "every failure path ends in ack" invariant (option (a) from the finding). Pinned by `ConsumerBaseTests.RepublishFailure_OriginalStillAcksAndNoThrow`.
**File:** `src/BuildingBlocks/KargoNet.Messaging/Consuming/ConsumerBase.cs:63`
**Issue:** `await _broker.PublishAsync(...)` inside the catch block is not guarded. The intended live wiring routes `IConsumerBroker.PublishAsync` through `MessagePublisher`, which throws `PublishNotConfirmedException` when the broker does not confirm. In that case the original delivery is never acked, the broker requeues it, and the handler runs again — i.e., the system silently falls back to nack-with-requeue semantics, which the locked spec explicitly rules out ("nack-with-requeue is never used"; "every failure path ends in ack").
**Fix:** Decide the policy explicitly and make it total. Two reasonable options, both preserving "always ack": (a) wrap the republish in try/catch, log the failed republish (with delivery tag + routing key), and ack the original — message lost, loud log, no requeue; (b) on republish failure, retry once with the DLQ routing key before acking. Option (a) is the smaller change; either way the `AckAsync` at line 67 must run on every path.

## Info

### IN-01: Null-forgiving on `eventType` silently accepts missing/`null`

**Resolution:** left as-is (info) — the malformed-body case that would expose this is now handled by the CR-02 DLQ routing; producer always writes `eventType`.
**File:** `src/BuildingBlocks/KargoNet.Contracts/Events/EventSerializer.cs:26`
**Issue:** `root.GetProperty("eventType").GetString()!` suppresses the nullable warning but produces an `EventEnvelope` with `EventType == null` when the property is absent or JSON `null`, violating the record's non-nullable contract downstream. Low impact because the producer (this repo) always writes it, and the malformed-body case is handled by the CR-02 fix.
**Fix:** `var eventType = root.GetProperty("eventType").GetString(); if (eventType is null) throw new JsonException("eventType is required");`

---

**Verified compliant (no findings):**
- Retry boundary `RetryPolicy.Decide` — matches "republish attempts 1 & 2, attempt ≥ 3 → DLQ" exactly (verified all four inputs 0–3 in `RetryPolicyTests`).
- Topology — `kargonet.shipments` topic exchange; `shipments.main`/`shipment`, `shipments.retry`/`shipment.retry` with `x-message-ttl=5000`, `x-dead-letter-exchange=kargonet.shipments`, `x-dead-letter-routing-key=shipment`; `shipments.dlq`/`shipment.dead` — all match the spec. `DeclareAsync` ordering is safe: RabbitMQ.Client 7.x serializes wire operations per channel, so `Task.WhenAll` over sequential calls preserves declaration-before-binding order.
- `MessagePublisher` — signature matches the spec's `PublishAsync(routingKey, body, headers)`; unconfirmed publishes throw.
- Consumer ack discipline on the happy path and well-formed failure path — ack always after success and after republish; no nacks anywhere.
- Tests — assert concrete header values, routing keys, ack tags, and wire-format strings; no tautologies. The DLQ test correctly asserts the original `x-retry-count` of 2 is preserved (accepted deviation).
- Known accepted deviations (7.2.2 confirm API naming, single bounded `JsonDocument` copy, DLQ retry-count preservation) — present as documented, not flagged.

---

_Reviewed: 2026-11-19T22:00:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
