---
phase: 04-tracking-api
reviewed: 2026-11-20T12:00:00Z
depth: deep
files_reviewed: 39
files_reviewed_list:
  - src/Services/Tracking.Api/Program.cs
  - src/Services/Tracking.Api/Tracking.Api.csproj
  - src/Services/Tracking.Api/appsettings.json
  - src/Services/Tracking.Api/appsettings.Development.json
  - src/Services/Tracking.Api/Consuming/ShipmentEventsConsumer.cs
  - src/Services/Tracking.Api/Consuming/ShipmentEventHandler.cs
  - src/Services/Tracking.Api/Data/TrackingDbContext.cs
  - src/Services/Tracking.Api/Data/TrackingDbContextFactory.cs
  - src/Services/Tracking.Api/Entities/ShipmentTracking.cs
  - src/Services/Tracking.Api/Entities/TrackingEvent.cs
  - src/Services/Tracking.Api/Entities/ProcessedEvent.cs
  - src/Services/Tracking.Api/Endpoints/TrackingEndpoints.cs
  - src/Services/Tracking.Api/Endpoints/DlqEndpoints.cs
  - src/Services/Tracking.Api/Tracking/ITrackingCache.cs
  - src/Services/Tracking.Api/Tracking/RedisTrackingCache.cs
  - src/Services/Tracking.Api/Tracking/TrackingDto.cs
  - src/Services/Tracking.Api/Tracking/TrackingUpdatedDto.cs
  - src/Services/Tracking.Api/Tracking/IShipmentUpdateBroadcaster.cs
  - src/Services/Tracking.Api/Realtime/TrackingHub.cs
  - src/Services/Tracking.Api/Realtime/SignalRShipmentUpdateBroadcaster.cs
  - src/Services/Tracking.Api/Dlq/IDlqAdmin.cs
  - src/Services/Tracking.Api/Dlq/RabbitDlqAdmin.cs
  - src/Services/Tracking.Api/Infra/LazyRedisConnection.cs
  - src/Services/Tracking.Api/Migrations/20261120020217_InitialCreate.cs
  - src/Services/Tracking.Api/Migrations/20261120020217_InitialCreate.Designer.cs
  - src/Services/Tracking.Api/Migrations/TrackingDbContextModelSnapshot.cs
  - src/BuildingBlocks/KargoNet.Messaging/Consuming/RabbitConsumerBroker.cs
  - src/BuildingBlocks/KargoNet.Messaging/Infra/LazyChannelFactory.cs
  - tests/KargoNet.Tracking.Tests/KargoNet.Tracking.Tests.csproj
  - tests/KargoNet.Tracking.Tests/TrackingTests.cs
  - tests/KargoNet.Tracking.Tests/Data/TrackingDbContextTests.cs
  - tests/KargoNet.Tracking.Tests/Consuming/ShipmentEventsConsumerTests.cs
  - tests/KargoNet.Tracking.Tests/Consuming/ShipmentEventsConsumerHostedTests.cs
  - tests/KargoNet.Tracking.Tests/Messaging/RabbitConsumerBrokerTests.cs
  - tests/KargoNet.Tracking.Tests/Realtime/TrackingHubTests.cs
  - tests/KargoNet.Tracking.Tests/Realtime/SignalRShipmentUpdateBroadcasterTests.cs
  - tests/KargoNet.Tracking.Tests/Api/TrackingEndpointTests.cs
  - tests/KargoNet.Tracking.Tests/Api/DlqEndpointTests.cs
  - tests/KargoNet.Tracking.Tests/Api/ShipmentEndpointTests.cs
findings:
  critical: 1
  warning: 6
  info: 7
  total: 14
  necessity: 1
status: issues_found
---

# Phase 04 (Tracking API): Code Review Report

**Reviewed:** 2026-11-20
**Depth:** deep
**Files Reviewed:** 39
**Status:** issues_found

## Summary

Adversarial review of the Phase 4 Tracking API implementation (commits `117876c`, `b3d5599`, `b7393f8` over base `bb54afd`). The architecture matches the locked design (04-CONTEXT.md D1–D9): additive `RabbitConsumerBroker`/`LazyChannelFactory` in `KargoNet.Messaging`, consumer mirrors `ConsumerBase`'s decision order verbatim, single-writer EF model with `ProcessedEvents` idempotency, lazy Redis/Rabbit connects, and a design-time migration consistent with the entity configuration.

**One BLOCKER** was found: the `DbUpdateException` backstop in `ShipmentEventHandler` misclassifies *any* DB write failure as an "already-processed duplicate" and acks it as success, which silently drops events under a plausible concurrency scenario — a direct violation of the "events are never lost" invariant. Six warnings cover runtime-robustness gaps (dead-channel recovery, shared-channel thread safety, unbounded consume fan-out, cache-failure fallback, unbounded replay loop, out-of-order status regression). Seven info items are minor.

All three locked invariants verified (below). The documented deviations (internal `Program`, Redis/Rabbit API-shape fixes in tests, `Logging.Abstractions` csproj line, `PublishAsync` header table) were checked and are exactly as documented — not re-flagged.

## Invariant Verification

1. **Phase 2 surface unchanged — PASS.** `git diff bb54afd..HEAD -- src/Services/Shipment.Api src/BuildingBlocks/KargoNet.Messaging/Consuming/ConsumerBase.cs src/BuildingBlocks/KargoNet.Messaging/Consuming/RetryPolicy.cs src/BuildingBlocks/KargoNet.Messaging/Topology/RabbitTopology.cs src/BuildingBlocks/KargoNet.Messaging/Topology/IConsumerBroker.cs src/BuildingBlocks/KargoNet.Contracts/Events/EventSerializer.cs` is empty. The `KargoNet.Messaging` diff is exactly two new files (`Consuming/RabbitConsumerBroker.cs`, `Infra/LazyChannelFactory.cs`) plus one `csproj` line (`Logging.Abstractions`) — purely additive. `RabbitConsumerBroker.ConsumeAsync` (54-74) routes every delivery through `ConsumerBase.HandleAsync`; the header names, retry-cap semantics, and always-ack contract match `ConsumerBase.cs:32-80` exactly.
2. **`src/Services/Shipment.Api/**` unchanged — PASS.** Empty diff.
3. **Every consumer failure path ends in ack — PASS (with caveats).** `ShipmentEventsConsumer.HandleDeliveryAsync` (97-160): decode inside the try, same decision switch as `ConsumerBase`, best-effort republish with the republish failure swallowed (134-137), unconditional ack (148-159). Caveats, not violations: (a) if `AckAsync` itself throws on a dead channel, the exception escapes to the fire-and-forget catch in `DeliveryConsumer` (logged); the broker redelivers on channel reset, so no silent loss. (b) the `default:` throw in the decision switch (151) is unreachable — `RetryPolicy.Decide` has exactly two cases — and mirrors `ConsumerBase`.

## Critical Issues

### CR-01: `DbUpdateException` backstop swallows ANY write failure as "already processed" → silent event loss

**File:** `src/Services/Tracking.Api/Consuming/ShipmentEventHandler.cs:139-144`
**Issue:** The `catch (DbUpdateException ex)` in `ApplyCoreAsync` treats *every* `DbUpdateException` as the D2 race backstop (a `ProcessedEvent.EventId` PK collision) and returns success — the message is then acked as processed and the event is lost. The D2 design is only correct when the conflict is on `PK_ProcessedEvents`; the catch never checks which constraint fired.

Loss scenario (plausible, not theoretical): two *different* events for the same tracking number (e.g. `shipment.created` followed milliseconds later by `shipment.status.changed`) are in flight concurrently — `DeliveryConsumer` fans out one `Task.Run` per delivery with no prefetch limit (see WR-03), so this is normal operation. Both pass the `ProcessedEvents.AnyAsync` check, both find no `ShipmentTracking` row, both `Add` one. The loser's `SaveChangesAsync` throws `DbUpdateException` (23505 on `PK_ShipmentTrackings`), is caught at line 139, logged as "duplicate race — treated as processed", and the status update is dropped forever with a success ack. Unrelated write failures are also swallowed: `destination` > 256 chars (22001 value-too-long), `trackingNumber` > 64 chars, etc. Each is a silent, acked data loss — a direct violation of the locked "events are never lost" invariant (TRK-01 / Phase 2 ack-invariant family).

The existing test `Race_backstop_duplicate_processed_marker_returns_success_without_rethrowing` (`ShipmentEventsConsumerTests.cs:242-268`) explicitly does not exercise this catch path (its comment says so) — the buggy code path is untested.

**Fix:** After rollback, re-check the marker in a fresh scope; only treat as duplicate if the marker now exists, otherwise rethrow so the pipeline routes the event to retry/DLQ:

```csharp
catch (DbUpdateException ex)
{
    await transaction.RollbackAsync();
    // Re-check in a fresh query: only a ProcessedEvent PK collision is a
    // true "already processed" race; any other write failure must surface.
    var alreadyProcessed = await _db.ProcessedEvents
        .AsNoTracking()
        .AnyAsync(e => e.EventId == eventId);
    if (!alreadyProcessed)
        throw;
    _logger.LogInformation(
        "Concurrent duplicate for {EventId} on {TrackingNumber}; treated as already processed.",
        eventId, trackingNumber);
}
```

## Warnings

### WR-01: `LazyChannelFactory` caches a dead channel forever — consume loop wedged after broker disconnect

**File:** `src/BuildingBlocks/KargoNet.Messaging/Infra/LazyChannelFactory.cs:22-41` (with `src/Services/Tracking.Api/Consuming/ShipmentEventsConsumer.cs:61-95`)
**Issue:** The factory never observes channel/connection shutdown. After a broker disconnect, the in-flight `BasicConsumeAsync` registration has already completed (shutdown only stops dispatch), so `ConsumeAsync` returns normally. The `ExecuteAsync` loop then calls `GetChannelAsync()` → returns the cached *dead* channel → `RabbitTopology.DeclareAsync` throws `OperationInterruptedException` → catch → 2 s delay → same dead channel → infinite failure loop. The consumer can never recover from a broker disconnect without a process restart. (The D1 "host never dies" property holds — the process stays up, spinning.)
**Fix:** Reset the cached channel on shutdown:

```csharp
var connection = await factory.CreateConnectionAsync();
var channel = await connection.CreateChannelAsync();
Model.HandleModelShutdown += (_, _) =>
{
    lock (_gate)
    {
        _channel = null;
        _connection = null;
    }
};
```

(or at minimum check `channel.IsOpen` before returning the cached channel).

### WR-02: Singleton `IChannel` shared across threads — consumer acks race with DLQ-admin operations

**File:** `src/Services/Tracking.Api/Program.cs:31-33`; `src/BuildingBlocks/KargoNet.Messaging/Consuming/RabbitConsumerBroker.cs:28-32`; `src/Services/Tracking.Api/Dlq/RabbitDlqAdmin.cs:27-60`
**Issue:** `LazyChannelFactory` is registered as a singleton and hands out **one** `IChannel`. `RabbitConsumerBroker` uses it for ack/republish from `Task.Run` delivery threads, while `RabbitDlqAdmin` uses the same channel for `BasicGet`/`BasicPublish` from HTTP request threads (`GET /api/admin/dlq`, `POST /api/admin/dlq/replay`). RabbitMQ.Client channels are not safe for concurrent use from multiple threads — concurrent frame writes corrupt the channel state and surface as `OperationInterruptedException` / protocol errors on in-flight acks. Any admin call while deliveries are flowing is a data-integrity risk for the ack stream.
**Fix:** Open a short-lived channel per operation in the admin (and generally prefer per-operation channels over a shared one):

```csharp
public async Task<int> ReplayAsync(CancellationToken ct)
{
    var channel = await _channels.GetChannelAsync();
    try
    {
        // ... BasicGet / BasicPublish loop on this dedicated channel
    }
    finally { channel.Dispose(); }
}
```

### WR-03: No QoS/prefetch on the consume channel → unbounded concurrent deliveries

**File:** `src/BuildingBlocks/KargoNet.Messaging/Consuming/RabbitConsumerBroker.cs:54-74` (no `BasicQosAsync`), `76-125` (`Task.Run` per delivery)
**Issue:** `ConsumeAsync` never sets `prefetchCount`, so the broker has no flow control (RabbitMQ default: unlimited). `DeliveryConsumer.HandleBasicDeliverAsync` then fires one unbounded `Task.Run` per delivery, each opening a DI scope + `TrackingDbContext` + transaction. A burst of N messages produces N concurrent handlers; past the Npgsql pool ceiling (default 100) healthy events fail with pool-timeout exceptions and are routed to the retry queue — i.e. load itself pushes good events into the retry path. Unbounded concurrent processing is also what makes the CR-01 race reachable.
**Fix:**

```csharp
public async Task ConsumeAsync(string queue, Func<ulong, ReadOnlyMemory<byte>, IDictionary<string, object?>?, Task> handler,
    IChannel channel, CancellationToken ct)
{
    await channel.BasicQosAsync(prefetchCount: 10, ct);
    await channel.BasicConsumeAsync(queue, autoAck: false, noLocal: false, exclusive: false, handler, ct);
}
```

### WR-04: Redis failure (not a miss) → 500 on `GET /api/tracking/{trackingNumber}`; no EF fallback

**File:** `src/Services/Tracking.Api/Endpoints/TrackingEndpoints.cs:33-35`; `src/Services/Tracking.Api/Tracking/RedisTrackingCache.cs:24-29`
**Issue:** The cache-aside read has no failure path. `LazyRedisConnection` is lazy by design (D4), so the first `GetAsync` performs the connect — if Redis is down or slow, `ConnectAsync` throws and the endpoint returns 500 even though the row exists in EF. Likewise, a stored value that fails deserialization (`JsonException` at `RedisTrackingCache.cs:25-27`) 500s. The design's "miss → EF" fallback covers misses only; a cache *failure* is not a miss. With Redis in a degraded state the entire tracking read API is down.
**Fix:**

```csharp
TrackingUpdatedDto? cached = null;
try { cached = await cache.GetAsync(trackingNumber, ct); }
catch (Exception ex) { _logger.LogWarning(ex, "Tracking cache read failed for {TrackingNumber}; falling back to EF.", trackingNumber); }
if (cached is not null)
    return Results.Ok(ToDto(cached));
```

(and treat deserialization failure as a miss — delete the key and fall through to EF).

### WR-05: `RabbitDlqAdmin.ReplayAsync` — unbounded drain loop with no cancellation

**File:** `src/Services/Tracking.Api/Dlq/RabbitDlqAdmin.cs:34-60` (with `src/Services/Tracking.Api/Endpoints/DlqEndpoints.cs:21-22`)
**Issue:** `while (true)` drains the DLQ with no bound and no `CancellationToken` (the `IDlqAdmin` interface — locked design — has no ct parameter, and the endpoint's injected `ct` is unused). A large DLQ (e.g. hours of failed events) blocks the `POST /api/admin/dlq/replay` request indefinitely, holding the channel and the request open forever; a client timeout leaves the loop running orphaned.
**Fix:** plumb a `CancellationToken` through `IDlqAdmin` and break the loop on cancellation (or at minimum bound the batch and return a count so the client can page):

```csharp
while (true)
{
    ct.ThrowIfCancellationRequested();
    var delivery = await channel.BasicGetAsync(RabbitTopology.DlqQueue, autoAck: false, ct);
    if (delivery is null) break;
    // ...
}
```

### WR-06: Out-of-order event application can regress the read-model status

**File:** `src/Services/Tracking.Api/Consuming/ShipmentEventHandler.cs:90-104` (`ApplyCreatedAsync`), `109-122` (`ApplyStatusChangedAsync`)
**Issue:** Both mutators apply unconditionally, with no ordering guard. A late-arriving `shipment.created` resets an existing row to `Status = "Created"` and overwrites `CreatedAt`/`UpdatedAt` with the *older* `occurredAt` — regressing, e.g., a `PickedUp` status; symmetrically a delayed `shipment.status.changed` overwrites a newer status. Out-of-order delivery is reachable: a `created` event bounced through the retry queue (TTL-delayed redelivery) can land *after* its own `status.changed`; DLQ replay reintroduces old events for the same reason. The timeline table is ordered by `OccurredAt` so it stays correct, but the live read model (`/api/tracking/{n}` and the broadcast) can show a stale status.
**Fix:** guard the mutators with the event's `occurredAt` — skip (or log) when the event is older than the row's last-applied time:

```csharp
if (row is not null)
{
    if (occurredAt <= row.UpdatedAt)
    {
        _logger.LogInformation("Stale {EventType} for {TrackingNumber} (occurredAt {OccurredAt} <= {UpdatedAt}); skipping.",
            eventType, trackingNumber, occurredAt, row.UpdatedAt);
        return;
    }
    // ... existing mutate
}
```

## Info

### IN-01: `RedisTrackingCache` ignores its `CancellationToken` parameters

**File:** `src/Services/Tracking.Api/Tracking/RedisTrackingCache.cs:24-42`
**Issue:** All three methods accept `ct` but call `StringGetAsync`/`StringSetAsync`/`KeyDeleteAsync` without it. A cancelled HTTP request cannot abort an in-flight (or first-connect) Redis call.
**Fix:** pass `ct` through to the Redis calls.

### IN-02: The `DbUpdateException` race-backstop path is untested

**File:** `tests/KargoNet.Tracking.Tests/Consuming/ShipmentEventsConsumerTests.cs:242-268`
**Issue:** `Race_backstop_duplicate_processed_marker_returns_success_without_rethrowing` exercises the deterministic skip path, not the `DbUpdateException` catch in `ApplyCoreAsync` (the comment acknowledges InMemory can't force the collision). The exact code path carrying CR-01 is therefore untested; a fix for CR-01 should add a test that injects/intercepts a `DbUpdateException` and asserts rethrow-vs-accept on both a true duplicate and a foreign constraint violation.

### IN-03: Meaningless `throw` in fire-and-forget delivery task

**File:** `src/BuildingBlocks/KargoNet.Messaging/Consuming/RabbitConsumerBroker.cs:117-121`
**Issue:** The catch in `HandleBasicDeliverAsync` logs and rethrows, but the task is fire-and-forget (`_ = Task.Run`); the rethrow is unobservable — no one ever awaits the task, so the `throw` is inert and misleading (it reads as "exception propagates" when nothing propagates).
**Category:** necessity
**Evidence:** the only consumer of the task is the discard at line 115; .NET does not surface unobserved task exceptions to the process, so the `throw` has no observable effect in this stack.
**Fix:** drop the `throw`; keep only the log (and consider making the method `async void`-free by documenting the log-only contract).

### IN-04: `SeedAsync` returns a disposed `TrackingDbContext`

**File:** `tests/KargoNet.Tracking.Tests/Api/TrackingEndpointTests.cs:44-53`
**Issue:** `using var scope` disposes the scoped `TrackingDbContext` before the method returns it. Current callers ignore the return value, but any future use is an `ObjectDisposedException` trap.
**Fix:** change the signature to `Task` (drop the return) or obtain the context from a caller-owned scope.

### IN-05: `OpenChannelAsync` leaks the connection if channel creation fails

**File:** `src/BuildingBlocks/KargoNet.Messaging/Infra/LazyChannelFactory.cs:43-51`
**Issue:** If `CreateChannelAsync` throws after `CreateConnectionAsync` succeeded, the connection is not disposed; with `ShipmentEventsConsumer`'s 2-second retry loop, a broker that accepts connections but refuses channels would accumulate one leaked connection per attempt.
**Fix:** wrap in try/catch and dispose the connection on failure.

### IN-06: Timeline endpoint returns an unbounded list

**File:** `src/Services/Tracking.Api/Endpoints/TrackingEndpoints.cs:49-61`
**Issue:** `GET /api/tracking/{trackingNumber}/events` materializes every `TrackingEvent` row for the tracking number with no pagination. Fine for the Phase 5 feed scope, noted for when per-shipment event counts grow.

### IN-07: `RabbitConsumerBroker` registered twice

**File:** `src/Services/Tracking.Api/Program.cs:32-33`
**Issue:** `AddSingleton<RabbitConsumerBroker>()` plus `AddSingleton<IConsumerBroker, RabbitConsumerBroker>()` registers the same singleton twice (harmless — same instance — but redundant; the interface registration alone suffices since nothing resolves the concrete type).

## Verdict

**issues_found** — 1 BLOCKER (CR-01), 6 WARNING (WR-01..WR-06), 7 INFO (IN-01..IN-07; 1 necessity).

CR-01 must be fixed before verification: it converts a plausible concurrency window into permanent, acked event loss, violating the locked "events are never lost" invariant. WR-01/WR-02/WR-03 are live-broker robustness gaps that will all surface in the Phase 6 live e2e and are cheap to fix now while the design is fresh; WR-04/WR-05/WR-06 are correctness-adjacent read-model/availability issues worth fixing in this phase. All three invariants hold; the documented deviations are exactly as documented.

---

_Reviewed: 2026-11-20_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: deep_
