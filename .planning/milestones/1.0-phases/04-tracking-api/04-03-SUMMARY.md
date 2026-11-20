---
phase: 04-tracking-api
plan: 03
subsystem: tracking-realtime-consume
tags: [signalr, rabbitmq, hosted-service, backgroundservice, aspnet-hosting, xunit]

# Dependency graph
requires: [04-01, 04-02]
provides:
  - "TrackingHub — SignalR hub with JoinTrackingGroup/LeaveTrackingGroup per tracking number (D4)"
  - "SignalRShipmentUpdateBroadcaster — implements the 04-01-frozen IShippingUpdateBroadcaster seam: ShipmentUpdated to Clients.All + the tracking-number group"
  - "RabbitConsumerBroker — lazy IConsumerBroker (AckAsync/PublishAsync from the frozen interface, ConsumeAsync as a class member, autoAck: false, always-ack delivery pipeline) over the 04-02 LazyChannelFactory"
  - "ShipmentEventsConsumer as a BackgroundService: declare topology, consume shipments.main, per-delivery scoped pipeline, 2 s retry loop on channel outage (D1 / T-04-08)"
  - "Program.cs final host: hosted consumer registration, AddSignalR, broadcaster seam registration, /hubs/tracking endpoint"
affects: [06]

actuals:
  tokens: 15300
  tasks: 3
  commits: 1
  plan_head_before: b3d5599
  plan_head_after: b7393f8

tech-stack:
  added:
    - "Microsoft.Extensions.Logging.Abstractions (KargoNet.Messaging) — ILogger<RabbitConsumerBroker> ctor dependency"
    - "ASP.NET Core SignalR (built into the 10.0.15 shared framework; AddSignalR + MapHub)"
  patterns:
    - "Broadcaster seam: the 04-01-frozen IShippingUpdateBroadcaster is implemented in the API project (SignalRShipmentUpdateBroadcaster) and registered scoped — the consume pipeline never touches SignalR types"
    - "Lazy consume: nothing connects at construction; the channel opens on the first ConsumeAsync through LazyChannelFactory, and the BackgroundService loop turns every outage into a 2 s backoff retry (T-04-08)"
    - "Delivery pipeline always acks: the broker's DeliveryConsumer acks on handler success AND failure, so a poisoned message can never block the queue (Phase 2 invariant)"

key-files:
  created:
    - src/Services/Tracking.Api/Realtime/TrackingHub.cs
    - src/Services/Tracking.Api/Realtime/SignalRShipmentUpdateBroadcaster.cs
    - src/BuildingBlocks/KargoNet.Messaging/Consuming/RabbitConsumerBroker.cs
    - tests/KargoNet.Tracking.Tests/Realtime/TrackingHubTests.cs
    - tests/KargoNet.Tracking.Tests/Realtime/SignalRShipmentUpdateBroadcasterTests.cs
    - tests/KargoNet.Tracking.Tests/Messaging/RabbitConsumerBrokerTests.cs
    - tests/KargoNet.Tracking.Tests/Consuming/ShipmentEventsConsumerHostedTests.cs
  modified:
    - src/Services/Tracking.Api/Consuming/ShipmentEventsConsumer.cs
    - src/Services/Tracking.Api/Program.cs
    - src/BuildingBlocks/KargoNet.Messaging/KargoNet.Messaging.csproj
    - tests/KargoNet.Tracking.Tests/Consuming/ShipmentEventsConsumerTests.cs

key-decisions:
  - "RabbitConsumerBroker keeps the frozen IConsumerBroker surface (AckAsync/PublishAsync) and exposes ConsumeAsync only as a class member — the Phase 2 interface is untouched"
  - "ConsumeAsync uses autoAck: false; the DeliveryConsumer acks after the handler task settles (success or failure), preserving the every-failure-paths-end-in-ack invariant"
  - "PublishAsync always carries a header table on the wire — an empty Dictionary<string, object?> when the caller passes null headers"
  - "ShipmentEventsConsumer is a BackgroundService with a while-loop: GetChannelAsync → RabbitTopology.DeclareAsync → ConsumeAsync; any failure logs, waits 2 s, and retries until the stopping token fires"
  - "The hosted consumer's delivery handler resolves the handler + cache + broadcaster from a fresh DI scope (using var scope via IServiceScopeFactory), so the long-lived hosted service never pins a scoped context"

patterns-established:
  - ".NET 10 BackgroundService test pattern: StartAsync returns once the service has started — the lifetime task is the new public ExecuteTask property; tests must await consumer.ExecuteTask to observe loop termination"
  - ".NET 10 SignalR test fakes: Hub.Context is a public settable HubCallerContext (abstract — fake it with a FixedCallerContext subclass), Hub.Groups is a public settable IGroupManager, and IHubContext<T>.Clients is the NON-generic IHubClients whose selectors return IClientProxy (SendCoreAsync(string, object?[], CancellationToken))"
  - ".NET 10 DI: the root provider registers IServiceScopeFactory as a built-in constant call site — resolve it with provider.GetRequiredService<IServiceScopeFactory>() (a direct cast does not compile)"
  - "RabbitMQ.Client 7.2.2 IChannel fakes (extending the 04-02 FakeChannel): BasicGetAsync returns Task<BasicGetResult> and QueueDeclarePassiveAsync returns Task<QueueDeclareOk> (Task, not ValueTask); IAsyncBasicConsumer has five Handle*Async members plus a Channel property"

requirements-completed: [TRK-03]

coverage:
  - id: T1
    description: "TrackingHub JoinTrackingGroup/LeaveTrackingGroup manage per-tracking-number membership; SignalRShipmentUpdateBroadcaster sends ShipmentUpdated to all clients AND the tracking-number group"
    verification:
      - kind: unit
        ref: "TrackingHubTests.JoinTrackingGroup_adds_connection_to_the_tracking_number_group, TrackingHubTests.LeaveTrackingGroup_removes_connection_from_the_tracking_number_group, SignalRShipmentUpdateBroadcasterTests.BroadcastAsync_sends_ShipmentUpdated_to_all_clients_and_the_tracking_group — pass"
    human_judgment: false
  - id: T2
    description: "RabbitConsumerBroker: lazy construction (no channel at ctor), AckAsync → BasicAck, PublishAsync → BasicPublish with header table (empty table when null), ConsumeAsync → BasicConsume with manual ack"
    verification:
      - kind: unit
        ref: "RabbitConsumerBrokerTests.Constructor_does_not_fetch_a_channel, RabbitConsumerBrokerTests.AckAsync_acks_single_delivery_without_multiple, RabbitConsumerBrokerTests.PublishAsync_publishes_to_exchange_and_routing_key_with_headers, RabbitConsumerBrokerTests.PublishAsync_without_headers_sends_empty_header_table, RabbitConsumerBrokerTests.ConsumeAsync_consumes_queue_with_manual_ack — pass (FakeChannel : IChannel)"
    human_judgment: false
  - id: T3
    description: "ShipmentEventsConsumer hosted loop survives a channel outage (retry, never dies) and stops on cancellation; the per-delivery pipeline applies the read model, dedupes redeliveries, routes unknown/handling failures to retry then DLQ, and always acks"
    verification:
      - kind: unit
        ref: "ShipmentEventsConsumerHostedTests.Consume_loop_survives_channel_outage_and_stops_on_cancellation, ShipmentEventsConsumerTests.{Created_body_applies_read_model_timeline_and_marker_and_side_effects, Redelivered_same_event_id_is_applied_exactly_once_and_ackd, Status_changed_body_updates_status_and_appends_timeline_row, Unknown_event_type_routes_to_retry_then_dlq_and_always_ackd, Handler_failure_routes_to_retry_then_dlq_and_always_ackd, Race_backstop_duplicate_processed_marker_returns_success_without_rethrowing} — pass"
    human_judgment: false

duration: 200min
completed: 2026-11-20
status: complete
---

# Phase 04 Plan 03: Realtime Tracking Hub + RabbitConsumerBroker + Hosted Consume Loop Summary

**SignalR tracking hub with per-tracking-number groups, a lazy RabbitConsumerBroker over the frozen IConsumerBroker surface, and the ShipmentEventsConsumer converted to a BackgroundService with a 2 s outage-retry consume loop — proven by 15 unit tests with an InMemory scope, fake channels, and .NET 10 SignalR fakes, no live services**

## Performance

- **Duration:** ~200 min
- **Tasks:** 3
- **Files modified:** 7 created, 4 modified

## Accomplishments

- `TrackingHub` — SignalR hub with `JoinTrackingGroup(trackingNumber)` / `LeaveTrackingGroup(trackingNumber)` (D4); `SignalRShipmentUpdateBroadcaster` implements the 04-01-frozen `IShippingUpdateBroadcaster` seam via `IHubContext<TrackingHub>` and broadcasts `ShipmentUpdated` to `Clients.All` + `Clients.Group(trackingNumber)` (TRK-03 / D4)
- `RabbitConsumerBroker` in `KargoNet.Messaging` — implements the frozen `IConsumerBroker` (`AckAsync` → `BasicAck`, `PublishAsync` → `BasicPublish` with header table) and adds `ConsumeAsync(queue, handler, ct)` as a class member → `BasicConsume` with `autoAck: false`; the `DeliveryConsumer` acks on handler success AND failure (poison messages never block the queue); lazy — nothing connects until first use through the 04-02 `LazyChannelFactory`
- `ShipmentEventsConsumer` converted plain class → `BackgroundService`: loop of `GetChannelAsync` → `RabbitTopology.DeclareAsync` → `ConsumeAsync(shipments.main, …)`; any failure logs a warning, waits 2 s, retries; exits cleanly on the stopping token (D1 / T-04-08); the delivery handler resolves scoped services (handler, cache, broadcaster) in a fresh DI scope per delivery
- `Program.cs` final host wiring: `AddSignalR()`, `AddSingleton<RabbitConsumerBroker>` + `IConsumerBroker` alias, scoped `IShippingUpdateBroadcaster` → `SignalRShipmentUpdateBroadcaster`, `AddHostedService<ShipmentEventsConsumer>()`, `app.MapHub<TrackingHub>("/hubs/tracking")`
- 15 new tests across 5 files: 2 hub membership, 1 broadcaster fan-out, 5 broker (lazy ctor, ack, publish with/without headers, consume manual-ack), 1 hosted loop outage survival, 6 consumer pipeline (the 04-01 test file converted to the hosted-service ctor per the plan's conversion gate)
- Full suite green: 68/68 in `KargoNet.Tracking.Tests` (53 pre-existing + 15 new); solution build 0 warnings / 0 errors

## Task Commits

1. **Task 1: Realtime seam (hub + broadcaster)** - `b7393f8` (plan commit)
2. **Task 2: RabbitConsumerBroker + hosted consumer + Program DI** - `b7393f8` (plan commit)
3. **Task 3: Tests (5 files, 15 tests)** - `b7393f8` (plan commit)

## Files Created/Modified

- `src/Services/Tracking.Api/Realtime/TrackingHub.cs` — group join/leave hub
- `src/Services/Tracking.Api/Realtime/SignalRShipmentUpdateBroadcaster.cs` — seam implementation
- `src/BuildingBlocks/KargoNet.Messaging/Consuming/RabbitConsumerBroker.cs` — lazy broker + ConsumeAsync + always-ack DeliveryConsumer
- `src/Services/Tracking.Api/Consuming/ShipmentEventsConsumer.cs` — BackgroundService convert
- `src/Services/Tracking.Api/Program.cs` — hosted service, SignalR, hub mapping
- `src/BuildingBlocks/KargoNet.Messaging/KargoNet.Messaging.csproj` — `Microsoft.Extensions.Logging.Abstractions` (deviation 1)
- `tests/KargoNet.Tracking.Tests/Realtime/{TrackingHubTests,SignalRShipmentUpdateBroadcasterTests}.cs` — 3 tests + .NET 10 fakes
- `tests/KargoNet.Tracking.Tests/Messaging/RabbitConsumerBrokerTests.cs` — 5 tests + `FakeChannel : IChannel`
- `tests/KargoNet.Tracking.Tests/Consuming/ShipmentEventsConsumerHostedTests.cs` — outage-survival loop test
- `tests/KargoNet.Tracking.Tests/Consuming/ShipmentEventsConsumerTests.cs` — 04-01 tests converted to the hosted ctor (6 tests)

## Decisions Made

- The frozen `IConsumerBroker` stays 2-method; `ConsumeAsync` is a class member of `RabbitConsumerBroker` only
- `autoAck: false` + ack-after-handler-settles keeps every failure path ending in an ack (Phase 2 invariant, threat T-04-09)
- The consume loop retries every failure (including "no reachable broker" — a Phase 6 deferral condition) with a 2 s delay instead of stopping the host
- Scoped services are resolved per delivery from `IServiceScopeFactory` — the hosted service never pins a scope

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking issue] `Microsoft.Extensions.Logging.Abstractions` added to KargoNet.Messaging.csproj**
- **Found during:** Task 2 (broker ctor)
- **Issue:** The plan-mandated `ILogger<RabbitConsumerBroker>` ctor dependency was not resolvable — the building block referenced no logging abstractions package
- **Fix:** added `Microsoft.Extensions.Logging.Abstractions` (framework-aligned version, no pin conflict)
- **Files modified:** src/BuildingBlocks/KargoNet.Messaging/KargoNet.Messaging.csproj
- **Verification:** build 0/0
- **Committed in:** b7393f8

**2. [Rule 1 - Bug] .NET 10 API adaptations in the new test files**
- **Found during:** Task 3 (compile + test runs)
- **Issue:** Several .NET 10 / RabbitMQ 7.2.2 signature facts differ from pre-10 training knowledge: `BackgroundService.StartAsync` no longer returns the execute task (the lifetime task is the new public `ExecuteTask` property) — the hosted test must `await consumer.ExecuteTask` to observe loop termination; `Hub.Context` is a public settable `HubCallerContext` (abstract, faked via `FixedCallerContext`), `Hub.Groups` is a settable `IGroupManager`, and `IHubContext<T>.Clients` returns the NON-generic `IHubClients` whose selectors return `IClientProxy` (single `SendCoreAsync(string, object?[], CancellationToken)`); the root DI provider registers `IServiceScopeFactory` as a built-in constant call site (resolve via `GetRequiredService`, a direct cast does not compile); the 7.2.2 `IChannel` fake needs `Task<BasicGetResult> BasicGetAsync` and `Task<QueueDeclareOk> QueueDeclarePassiveAsync` (Task, not ValueTask), and `IAsyncBasicConsumer` has five `Handle*Async` members plus `Channel`
- **Fix:** tests written against the ref-pack-verified 10.0.15 / 7.2.2 signatures; CS8613 nullability warning on `BasicGetResult?` accepted (identical pre-existing warning in `Dlq/RabbitDlqAdminTests.cs`)
- **Files modified:** tests/KargoNet.Tracking.Tests/Realtime/TrackingHubTests.cs, tests/KargoNet.Tracking.Tests/Realtime/SignalRShipmentUpdateBroadcasterTests.cs, tests/KargoNet.Tracking.Tests/Messaging/RabbitConsumerBrokerTests.cs, tests/KargoNet.Tracking.Tests/Consuming/ShipmentEventsConsumerHostedTests.cs, tests/KargoNet.Tracking.Tests/Consuming/ShipmentEventsConsumerTests.cs
- **Verification:** build 0/0; all 15 Task 3 tests pass
- **Committed in:** b7393f8

**3. [Rule 1 - Bug] PublishAsync null headers left `BasicProperties.Headers` null**
- **Found during:** Task 3 (`PublishAsync_without_headers_sends_empty_header_table` failed — `Assert.Empty` on a null table)
- **Issue:** `if (headers is not null) properties.Headers = …` leaves the wire header table null, but the RabbitMQ wire format always carries a table
- **Fix:** `properties.Headers = headers?.ToDictionary(…) ?? new Dictionary<string, object?>()` — an empty table is sent when the caller publishes without headers
- **Files modified:** src/BuildingBlocks/KargoNet.Messaging/Consuming/RabbitConsumerBroker.cs
- **Verification:** all 15 Task 3 tests pass
- **Committed in:** b7393f8

---

**Total deviations:** 3 auto-fixed (1 package reference, 2 API-shape/test bugs)

## Verification Evidence

```
$ ~/.dotnet/dotnet build /home/cek/projects/KargoNet/KargoNet.sln
Build succeeded. 0 Warning(s) 0 Error(s)

$ ~/.dotnet/dotnet test tests/KargoNet.Tracking.Tests/KargoNet.Tracking.Tests.csproj --filter "FullyQualifiedName~SignalRShipmentUpdateBroadcasterTests|FullyQualifiedName~TrackingHubTests|FullyQualifiedName~RabbitConsumerBrokerTests|FullyQualifiedName~ShipmentEventsConsumerHostedTests|FullyQualifiedName~ShipmentEventsConsumerTests"
Passed!  - Failed: 0, Passed: 15, Skipped: 0, Total: 15

$ ~/.dotnet/dotnet test /home/cek/projects/KargoNet/KargoNet.sln
Passed!  - Failed: 0, Passed: 68, Skipped: 0, Total: 68

$ grep -c "AddHostedService<ShipmentEventsConsumer>" src/Services/Tracking.Api/Program.cs
1
$ grep -c "MapHub<TrackingHub>" src/Services/Tracking.Api/Program.cs
1
```

---
*Phase: 04-tracking-api*
*Completed: 2026-11-20*

## Self-Check: PASSED

## Review Fixes

Phase 4 code review (`.planning/phases/04-tracking-api/REVIEW.md`) — 1 BLOCKER + 6 WARNING fixed, INFO items dispositioned. Suite: 68 baseline → 78 tests (10 new); build 0 warnings / 0 errors.

| Finding | Status | Fix | Proving test(s) | Commit |
|---|---|---|---|---|
| CR-01 | fixed | `ShipmentEventHandler` catch path now rolls back and re-checks the `ProcessedEvents` marker: a committed rival marker means the D2 race (success, no retry); a missing marker is a genuine `DbUpdateException` and is rethrown so the consumer routes retry → DLQ like every other handler failure | `ShipmentEventsConsumerTests.Race_backstop_committed_marker_is_ackd_as_success_without_retry`, `ShipmentEventsConsumerTests.DbUpdateException_without_committed_marker_is_rethrown_and_routed_to_retry` | `1e9f05c` |
| WR-01 | fixed | `LazyChannelFactory` reopens a shut-down channel on the next `GetChannelAsync` (channel-shutdown handler nulls the cached channel) and adds `CreateChannelAsync()` for per-operation channels (caller disposes) | `LazyChannelFactoryTests` (3 tests: caches while open, reopens after close, distinct per-operation channels) | `07f9b63` |
| WR-02 | fixed | `RabbitConsumerBroker.AckAsync`/`PublishAsync` and `RabbitDlqAdmin.CountAsync`/`ReplayAsync` now use per-operation channels from `CreateChannelAsync()` disposed in `finally` — a dead channel can never poison an ack; the inert `throw;` in the delivery catch was removed | `RabbitDlqAdminTests.ReplayAsync_stops_on_cancellation_and_disposes_the_channel` (+ `Assert.True(channel.Disposed)` in both replay tests) | `71594f1` |
| WR-03 | fixed | `ConsumeAsync` sets `BasicQos(prefetchSize: 0, prefetchCount: 10, global: false)` before `BasicConsume` — bounded in-flight deliveries per consumer | `RabbitConsumerBrokerTests.ConsumeAsync_sets_prefetch_before_consuming` | `def028b` |
| WR-04 | fixed | `GET /api/tracking/{trackingNumber}` wraps cache read + repopulation in try/catch and degrades to the EF row on cache failure (logged, 200 from EF); `RedisTrackingCache.GetAsync` also drops a corrupt (un-deserializable) cached payload so the next read repopulates; `SeedAsync` helper trimmed to `Task` (IN-04) | `TrackingEndpointTests.Get_degrades_to_EF_when_the_cache_fails` (always-failing `ITrackingCache` fake) | `b644aae` |
| WR-05 | fixed | `IDlqAdmin.ReplayAsync` takes a `CancellationToken`, checked at the top of the replay loop and passed to `BasicGetAsync`; the DLQ replay endpoint forwards its request token | `RabbitDlqAdminTests.ReplayAsync_stops_on_cancellation_and_disposes_the_channel` | `cb5255d` |
| WR-06 | fixed | `ShipmentEventHandler.ApplyCoreAsync` skips stale events: an existing row with `UpdatedAt >= occurredAt` is not regressed (no row change, no timeline entry) — a late `shipment.created` can no longer overwrite a `PickedUp` row; freshly created rows are exempt | `ShipmentEventsConsumerTests.Late_created_event_does_not_regress_an_existing_row`, `ShipmentEventsConsumerTests.Late_status_change_does_not_overwrite_a_newer_status` | `5b797ac` |
| IN-01 | accepted | Pinned StackExchange.Redis 2.13.23 `IDatabase` string/key APIs have no `CancellationToken` overloads, so the cache calls cannot be cancelled at the driver level; the `ct` parameters stay on `ITrackingCache` for interface stability. Cache ops are single round-trips behind a lazy connection — low exposure | — | — |
| IN-02 | fixed | Folded into CR-01 (marker re-check in the catch path) | see CR-01 | `1e9f05c` |
| IN-03 | fixed | Folded into WR-02 (per-operation channel for acks) | see WR-02 | `71594f1` |
| IN-04 | fixed | Folded into WR-04 (`SeedAsync` returns `Task`) | see WR-04 | `b644aae` |
| IN-05 | fixed | Folded into WR-01 (factory reopen on shutdown) | see WR-01 | `07f9b63` |
| IN-06 | accepted | Unbounded EF list on the tracking events endpoint is Phase 5 pagination scope (roadmap TRK-05); the endpoint is internal-facing and key-scoped by tracking number | — | — |
| IN-07 | rejected | Reviewer is wrong: `ShipmentEventsConsumer`'s ctor takes the concrete `RabbitConsumerBroker` (for `ConsumeAsync`), so BOTH the `AddSingleton<RabbitConsumerBroker>` and the `IConsumerBroker` alias registrations are required; removing the alias breaks consumer construction | — | — |

### Verification (post-fix)

```
$ ~/.dotnet/dotnet build /home/cek/projects/KargoNet/KargoNet.sln
Build succeeded. 0 Warning(s) 0 Error(s)

$ ~/.dotnet/dotnet test /home/cek/projects/KargoNet/KargoNet.sln
Passed!  - Failed: 0, Passed: 78, Skipped: 0, Total: 78
```

