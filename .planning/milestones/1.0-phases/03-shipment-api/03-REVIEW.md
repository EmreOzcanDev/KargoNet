---
phase: 03-shipment-api
reviewed: 2026-11-20T12:00:00Z
depth: standard
files_reviewed: 18
files_reviewed_list:
  - src/Services/Shipment.Api/Data/ShipmentDbContext.cs
  - src/Services/Shipment.Api/Data/ShipmentDbContextFactory.cs
  - src/Services/Shipment.Api/Entities/Shipment.cs
  - src/Services/Shipment.Api/Entities/OutboxMessage.cs
  - src/Services/Shipment.Api/Migrations/20261119232650_InitialCreate.cs
  - src/Services/Shipment.Api/Migrations/20261119232650_InitialCreate.Designer.cs
  - src/Services/Shipment.Api/Migrations/ShipmentDbContextModelSnapshot.cs
  - src/Services/Shipment.Api/Dto/ShipmentDto.cs
  - src/Services/Shipment.Api/Endpoints/Shipments.cs
  - src/Services/Shipment.Api/Publishing/OutboxPublisherService.cs
  - src/Services/Shipment.Api/Program.cs
  - src/Services/Shipment.Api/Shipment.Api.csproj
  - src/BuildingBlocks/KargoNet.Messaging/Publishing/LazyOutboxChannelFactory.cs
  - src/BuildingBlocks/KargoNet.Messaging/KargoNet.Messaging.csproj
  - tests/KargoNet.Tracking.Tests/Data/ShipmentDbContextTests.cs
  - tests/KargoNet.Tracking.Tests/Api/ShipmentEndpointTests.cs
  - tests/KargoNet.Tracking.Tests/Publishing/OutboxPublisherServiceTests.cs
  - tests/KargoNet.Tracking.Tests/KargoNet.Tracking.Tests.csproj
findings:
  critical: 1
  warning: 4
  info: 1
  total: 6
  necessity: 0
status: resolved
---

# Phase 03: Code Review Report

**Reviewed:** 2026-11-20T12:00:00Z
**Depth:** standard
**Files Reviewed:** 18
**Status:** resolved

## Summary

Reviewed the Phase 3 implementation (commits `3f03108`, `69636e9`, `22b6cf3`) against the locked design in `03-CONTEXT.md`: EF model + migration, four REST endpoints with one-transaction-per-write, the outbox publisher, the lazy broker channel, and the test suites. Cross-referenced Phase 2 surfaces (`MessagePublisher`, `RabbitOutboxChannel`, `RabbitTopology`, `EventSerializer`) and verified the wire-format round-trip (endpoint `PayloadOptions` is option-for-option identical to `EventSerializer` — camelCase + `JsonStringEnumConverter` — so stored `PayloadJson` round-trips through `EventSerializer.Deserialize`, as the tests assert).

**Model/migration consistency: PASS.** `InitialCreate`, the Designer, and the model snapshot all agree with the entities and the locked schema: uuid PKs, `varchar(64)` unique `TrackingNumber`, `varchar(256)` `Destination`, `text` string-converted statuses, `timestamptz` timestamps, and the `(Status, CreatedAt)` outbox index. Index names and column types match across all three generated files.

**Recorded deviations — judged all sound:**
- *dotnet-ef 10.0.15 pin* — tooling-only, no repo impact. Sound.
- *Mvc.Testing 10.0.15* — resolves MSB3277 against the net10 reference pack. Sound.
- *Direct EF Relational 10.0.15 pins in both csprojs* — correct; a pin in one project does not propagate to the other's restore graph. Sound.
- *`Database:Provider`/`Database:Name` config switch + InMemory package in the service csproj* — pragmatic and verified to work (the DI-swap alternative is genuinely dead in EF Core 10's options composition). Cost: a test-only package in the production project and a test-mode branch in `Program.cs`; acceptable at this phase, worth revisiting if more test seams appear. Sound.
- *Local `PayloadOptions` in `Shipments.cs`* — verified option-identical to `EventSerializer`. Sound, with a drift risk if the Phase 2 options ever change (kept out of findings since it is the recorded deviation).
- *Configuration.Abstractions 10.0.15 in KargoNet.Messaging* — matches the net10 pack version. Sound.
- *Positional `CreateChannelOptions(true, false)`* — verified against the RabbitMQ.Client 7.2.2 assembly XML docs: the first field is `PublisherConfirmationsEnabled`, so `(true, false)` enables publisher confirms as intended. Sound.

One operational note (not a finding): `appsettings.json` carries no `ConnectionStrings:ShipmentDb`, so a local `dotnet run` has no DB endpoint until Phase 6 compose/env provides one; the broker path has an `amqp://localhost` fallback in `LazyOutboxChannelFactory` but the DB path does not. Consistent with the locked design (Phase 6 provides the stack).

**Key concerns:** one critical defect in the publisher's per-row failure path (a single failed row in a batch silently disarms the `Published` mark for every remaining row in that batch, causing duplicate delivery), plus four warnings: a catch-all `DbUpdateException → 409` in POST, a DB failure in the poll loop taking down the whole host, a non-thread-safe lazy channel init, and POST persisting an empty tracking number.

## Critical Issues

### CR-01: `ChangeTracker.Clear()` on per-row failure detaches the rest of the batch — confirmed publishes are never marked `Published`, causing duplicate delivery

**File:** `src/Services/Shipment.Api/Publishing/OutboxPublisherService.cs:79-83`
**Issue:** `rows` (line 45-50) are *tracked* entities returned by `ToListAsync`. When any row's publish (or its save/commit) throws, the catch block calls `_db.ChangeTracker.Clear()` (line 82), which detaches **every** tracked entity — including all remaining rows in the current batch. For each subsequent row in that same batch:

1. `PublishAsync` succeeds (broker confirms);
2. `row.Status = OutboxStatus.Published` mutates a now-detached entity;
3. `SaveChangesAsync` (line 72) is a no-op — there are no tracked changes;
4. the row stays `Pending` in the database while its message was already delivered.

On the next 2 s poll the row is picked up again and **republished** → duplicate events to consumers. This directly violates the locked design ("On confirm: mark row `Published` + `PublishedAt` (own short transaction per row)") and the code's own comment ("a single failure cannot block the rest of the batch") — it is the exact opposite. `published++` (line 77) also over-reports for those rows. Any single failed publish in a 50-row batch (broker blip, unconfirmed publish) corrupts the marking for up to 49 more rows.

**Fix:**
```csharp
catch (Exception ex) when (ex is not OperationCanceledException)
{
    // No ChangeTracker.Clear() here: on publish failure the row is unmodified,
    // and clearing the whole tracker detaches the remaining rows in this
    // batch so their Published marks can never persist.
    _logger.LogWarning(ex, "Outbox publish failed for row {RowId}; leaving Pending for retry.", row.Id);
}
```
If a row's *save/commit* fails after its publish succeeded, leaving it tracked-and-Modified is the correct outcome: the next row's `SaveChangesAsync` will persist its `Published` mark (its publish had confirmed). If a targeted discard is ever needed, detach only the failed entity via `ChangeTracker.Entry(row).State = EntityState.Detached` — never the whole tracker.

Add a regression test: seed 3 pending rows, make row 1 fail and rows 2-3 confirm; assert rows 2 and 3 are `Published` after the poll.

**Resolution:** FIXED in `0c0cac1` — `ChangeTracker.Clear()` removed from the per-row failure path; remaining batch rows stay tracked and their `Published` marks persist. Regression test `FailedFirstRow_DoesNotDetachRemainingRows_TheyAreMarkedPublished` added (row 1 fails, rows 2-3 confirmed AND marked `Published`).

## Warnings

### WR-01: Broad `catch (DbUpdateException)` maps every database failure to 409 Conflict

**File:** `src/Services/Shipment.Api/Endpoints/Shipments.cs:75-86`
**Issue:** The race-backstop `catch (DbUpdateException) => Results.Conflict()` (line 82-85) catches *all* update failures, not just unique-constraint violations. Consequences: a 100-character `trackingNumber` or 257-character `destination` (column-length violation, SqlState 22001) returns **409** instead of 400; a transient connection failure during `SaveChangesAsync` also returns 409 to a caller whose write simply failed. 409 is a specific semantic (duplicate) and over-claiming it misleads clients and masks outages.

**Fix:**
```csharp
catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
{
    return Results.Conflict(); // unique-violation only (23505)
}
```
and validate lengths up front so over-long input is a proper 400:
```csharp
if (trackingNumber.Length > 64 || destination.Length > 256)
    return Results.BadRequest();
```

**Resolution:** FIXED in `b7eb4d3` — the backstop catch is now `catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })` (409 only on unique violation), with up-front validation returning 400 for `trackingNumber.Length > 64` / `destination.Length > 256`. Test `Create_shipment_with_over_long_tracking_number_or_destination_returns_400` added.

### WR-02: A database failure in the poll loop escapes `ExecuteAsync` and terminates the host

**File:** `src/Services/Shipment.Api/Publishing/OutboxPublisherService.cs:90-105`
**Issue:** `ExecuteAsync` catches only `OperationCanceledException` (line 96-99). The per-row `catch` (line 79) protects only the publish/save of an individual row; the batch query itself (`ToListAsync`, line 45-50) runs **outside** it. If Postgres is unreachable/restarting when a poll fires, that exception propagates out of `ExecuteAsync` — an unhandled exception in a hosted service stops the host, taking the REST API down with it. The locked design requires the API to keep serving REST + outbox writes without a reachable *broker*; the same resilience is missing for the database the publisher depends on, and a broker-down scenario plus a Postgres restart kills the process.

**Fix:**
```csharp
while (!stoppingToken.IsCancellationRequested)
{
    try
    {
        await PollOnceAsync(stoppingToken);
    }
    catch (OperationCanceledException)
    {
        break;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Outbox poll failed; retrying next interval.");
    }

    await Task.Delay(PollInterval, stoppingToken);
}
```

**Resolution:** FIXED in `0c0cac1` — `ExecuteAsync` now catches non-cancellation exceptions around `PollOnceAsync` (log + retry next interval), so a database failure no longer terminates the host. Test `PollFailure_DoesNotTerminateHostLoop_RetriesNextInterval` added (failing context, loop observed alive across the 2 s interval).

### WR-03: Lazy channel init is not thread-safe — concurrent first publishes leak a connection and channel

**File:** `src/BuildingBlocks/KargoNet.Messaging/Publishing/LazyOutboxChannelFactory.cs:23-24`
**Issue:** `var channel = _channel ?? await OpenChannelAsync(); _channel = channel;` is a check-then-act across an `await`. Two concurrent first-time `PublishAsync` calls both observe `_channel == null`, both open a connection + confirm channel, and whichever loses the race leaks its `IConnection`/`IChannel` (never closed, never cached). Today the sole caller is the single-threaded publisher loop, so this is latent — but the class is the registered singleton `IOutboxChannel` and the locked design explicitly calls for thread-safe lazy creation; any future second publisher (e.g. Phase 4/6) hits a real resource leak.

**Fix:**
```csharp
private readonly SemaphoreSlim _openLock = new(1, 1);
private RabbitOutboxChannel? _channel;

public async Task<PublishOutcome> PublishAsync(string exchange, string routingKey, ReadOnlyMemory<byte> body, IDictionary<string, object?>? headers)
{
    if (_channel is null)
    {
        await _openLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _channel ??= await OpenChannelAsync().ConfigureAwait(false);
        }
        finally { _openLock.Release(); }
    }
    return await _channel.PublishAsync(exchange, routingKey, body, headers).ConfigureAwait(false);
}
```
(Also dispose the loser's connection in the race path if the lock is not used.)

**Resolution:** FIXED in `15853ad` — lazy channel open is guarded by a `SemaphoreSlim`; concurrent first publishes re-check `_channel` under the lock instead of leaking a second connection/channel.

### WR-04: POST persists a shipment with an empty tracking number

**File:** `src/Services/Shipment.Api/Endpoints/Shipments.cs:37,44-46`
**Issue:** `request.TrackingNumber?.Trim() ?? string.Empty` allows a missing or whitespace-only tracking number to pass the duplicate pre-check and be persisted as `TrackingNumber = ""` (with a `shipment.created` event carrying `trackingNumber: ""`). The design pins "tracking number trimmed; duplicate → 409" and defines 400 semantics for the API, but the trimmed-empty case is left unvalidated — and it has teeth: the unique index means exactly one such empty-number shipment can ever exist, and every later empty POST 409s against it. A 400 on empty input is the correct behavior and costs one check.

**Fix:**
```csharp
var trackingNumber = request.TrackingNumber?.Trim() ?? string.Empty;
if (trackingNumber.Length == 0)
    return Results.BadRequest();
```

**Resolution:** FIXED in `b7eb4d3` — `CreateShipment` returns 400 when the trimmed `trackingNumber` is empty. Test `Create_shipment_with_empty_or_whitespace_tracking_number_returns_400` added (asserts nothing is persisted).

## Info

### IN-01: Publisher-confirm behavior after broker auto-recovery is unverified — outbox can wedge Pending forever

**File:** `src/BuildingBlocks/KargoNet.Messaging/Publishing/LazyOutboxChannelFactory.cs:33-37` (with `src/BuildingBlocks/KargoNet.Messaging/Publishing/RabbitOutboxChannel.cs:48-52`)
**Issue:** The channel is created with publisher confirms enabled (`CreateChannelOptions(true, false)` — verified against the 7.2.2 assembly) and cached for the process lifetime. When the broker dies and the client's automatic recovery reopens the connection, it is not established (live path is a Phase 6 deferral) that the recovered channel retains confirm mode. If recovery reopens the channel with default (non-confirm) options, `BasicAcksAsync` never fires again, every publish times out as unconfirmed, and every outbox row stays `Pending` permanently — silent wedge with no data loss but zero propagation. Phase 6 must verify confirm-mode survival across a broker restart (and that the factory declares/relies on the Phase 6 topology, per the locked design).

**Fix:** In Phase 6, add a compose-based test: publish → confirm, restart the broker, publish → confirm again; assert `PublishOutcome.IsConfirmed` remains true. If the client does not preserve confirms on recovery, recreate the channel (close + `CreateChannelAsync(new CreateChannelOptions(true, false))`) on connection-recovery events.

**Deferral:** DEFERRED to Phase 6 — confirm-mode survival across broker auto-recovery requires a live broker (compose), which does not exist until Phase 6 provides the stack; the factory already enables confirms at channel creation and Phase 6 will verify `PublishOutcome.IsConfirmed` after a broker restart.

---

_Reviewed: 2026-11-20T12:00:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
