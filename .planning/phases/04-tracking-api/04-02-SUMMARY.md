---
phase: 04-tracking-api
plan: 02
subsystem: tracking-cache-dlq
tags: [redis, cache-aside, dlq, rabbitmq, aspnet-endpoints, xunit]

# Dependency graph
requires: [04-01]
provides:
  - "RedisTrackingCache — StackExchange.Redis 2.13.23 implementation of the frozen ITrackingCache (tracking:{lowercase-number} keys, 10-min TTL, camelCase + JsonStringEnumConverter wire format)"
  - "LazyRedisConnection (SemaphoreSlim-guarded lazy ConnectionMultiplexer, `Redis` config key) and LazyChannelFactory (new KargoNet.Messaging infra, `RabbitMq` config key, virtual GetChannelAsync for test fakes)"
  - "GET /api/tracking/{trackingNumber} cache-aside endpoint (DB miss → warm cache) + GET /api/tracking/{trackingNumber}/events raw timeline endpoint"
  - "IDlqAdmin / RabbitDlqAdmin (passive QueueDeclare count + BasicGet autoAck replay loop) behind GET /api/admin/dlq + POST /api/admin/dlq/replay"
  - "Program.cs full host: InMemory/Npgsql switch, cache + DLQ DI, /health, both endpoint groups; public TestEntryPoint marker for WebApplicationFactory<T>"
affects: [04-03, 06]

actuals:
  tasks: 3
  commits: 1
  plan_head_before: 117876c
  plan_head_after: b3d5599

tech-stack:
  added:
    - "StackExchange.Redis 2.13.23 (Tracking.Api) — latest stable 2.x; 3.x line explicitly excluded by the phase pin"
  patterns:
    - "Lazy-connect infrastructure mirrors LazyOutboxChannelFactory: SemaphoreSlim(1,1) double-check, cached instance, config-key connection string with localhost fallback — nothing connects until first use"
    - "Cache-aside in the endpoint layer: cache hit short-circuits EF; miss reads the read model, warms the cache with a 10-minute TTL, then responds"
    - "DLQ replay via BasicGetAsync(autoAck: true) drain loop — messages leave the queue as they are republished to the main routing key with no x-retry-count header, so replays start with a fresh retry budget"

key-files:
  created:
    - src/Services/Tracking.Api/Tracking/TrackingDto.cs
    - src/Services/Tracking.Api/Tracking/RedisTrackingCache.cs
    - src/Services/Tracking.Api/Infra/LazyRedisConnection.cs
    - src/Services/Tracking.Api/Endpoints/TrackingEndpoints.cs
    - src/Services/Tracking.Api/Endpoints/DlqEndpoints.cs
    - src/Services/Tracking.Api/Dlq/IDlqAdmin.cs
    - src/Services/Tracking.Api/Dlq/RabbitDlqAdmin.cs
    - src/BuildingBlocks/KargoNet.Messaging/Infra/LazyChannelFactory.cs
    - tests/KargoNet.Tracking.Tests/Api/TrackingEndpointTests.cs
    - tests/KargoNet.Tracking.Tests/Api/DlqEndpointTests.cs
    - tests/KargoNet.Tracking.Tests/Dlq/RabbitDlqAdminTests.cs
  modified:
    - src/Services/Tracking.Api/Tracking.Api.csproj
    - src/Services/Tracking.Api/Program.cs

key-decisions:
  - "StackExchange.Redis pinned to 2.13.23 — the latest stable 2.x on NuGet (a bare `dotnet add` resolves 3.4.35, the 3.x line the phase rules exclude)"
  - "`LazyChannelFactory` (new, not Phase 2 surface) reads the `RabbitMq` config key with `amqp://localhost` fallback and exposes `virtual GetChannelAsync()` so tests can subclass it; non-sealed"
  - "`Program` stays `internal partial` (CS0433 constraint from 04-01); a public `TestEntryPoint` marker type is added so `WebApplicationFactory<T>` can target the Tracking host"
  - "`ITrackingCache` implemented as declared (04-01 frozen): values are serialized `TrackingUpdatedDto`; the endpoint maps `ShipmentTracking` → `TrackingDto` for the HTTP response and → `TrackingUpdatedDto` for the cache"

patterns-established:
  - "Test `FakeChannel : IChannel` for RabbitMQ.Client 7.2.2 — explicit-enough implementations of all 40+ members; the generic `BasicPublishAsync<TProperties>` (constraint `IReadOnlyBasicProperties, IAmqpHeader`) needs `where` clauses matching the interface; `QueueUnbindAsync` has both a 5- and a 6-parameter overload"
  - "Web-SDK test host pattern: `internal partial class Program` + public marker type + `WebApplicationFactory<T>` with `builder.ConfigureServices` fake swaps (`services.RemoveAll<T>()` lives in `Microsoft.Extensions.DependencyInjection.Extensions` in .NET 10)"

requirements-completed: [TRK-02, TRK-04, TRK-05]

coverage:
  - id: T1
    description: "GET /api/tracking/{n} — DB hit warms the cache and returns the DTO; second GET served from cache (DB mutation invisible); unknown number → 404; /events returns the ordered timeline for that tracking number only"
    verification:
      - kind: unit
        ref: "TrackingEndpointTests.Get_known_tracking_number_returns_dto_and_warms_cache, TrackingEndpointTests.Second_get_is_served_from_cache_not_the_database, TrackingEndpointTests.Get_unknown_tracking_number_returns_404, TrackingEndpointTests.Events_endpoint_returns_ordered_timeline_for_one_tracking_number — pass"
    human_judgment: false
  - id: T2
    description: "DLQ admin endpoints: GET /api/admin/dlq reports the queue count; POST /api/admin/dlq/replay drains and reports the replayed message count"
    verification:
      - kind: unit
        ref: "DlqEndpointTests.Admin_get_reports_dlq_count, DlqEndpointTests.Admin_replay_reports_replayed_count — pass (FakeDlqAdmin)"
    human_judgment: false
  - id: T3
    description: "RabbitDlqAdmin over a real IChannel contract: passive count via QueueDeclarePassive; replay republishes each DLQ body to kargonet.shipments / shipment with no x-retry-count header and returns the moved count; empty DLQ → 0"
    verification:
      - kind: unit
        ref: "RabbitDlqAdminTests.Count_async_reads_the_passive_message_count, RabbitDlqAdminTests.Replay_async_republishes_every_body_to_the_main_routing_key_and_returns_the_count, RabbitDlqAdminTests.Replay_async_on_an_empty_dlq_returns_zero — pass (FakeChannel : IChannel)"
    human_judgment: false

duration: 150min
completed: 2026-11-20
status: complete
---

# Phase 04 Plan 02: Cache-Aside Tracking Read Path + DLQ Admin Surface Summary

**Redis-backed cache-aside tracking endpoints (StackExchange.Redis 2.13.23) and a DLQ admin surface (passive count + replay) over a new LazyChannelFactory — proven by 9 unit tests with InMemory EF, a fake cache, and a full IChannel fake, no live services**

## Performance

- **Duration:** ~150 min
- **Tasks:** 3
- **Files modified:** 11 created, 2 modified

## Accomplishments

- `RedisTrackingCache` — implements the 04-01-frozen `ITrackingCache`; keys `tracking:{number.ToLowerInvariant()}`; values are `TrackingUpdatedDto` serialized camelCase + `JsonStringEnumConverter`; `StringSetAsync(key, json, 10 min)` TTL; `IsNull`-guarded deserialize
- `LazyRedisConnection` — SemaphoreSlim-guarded lazy `ConnectionMultiplexer` on the `Redis` config key (localhost fallback); `LazyChannelFactory` — same pattern for RabbitMQ channels on the `RabbitMq` key, `virtual GetChannelAsync()` for test fakes
- `GET /api/tracking/{trackingNumber}` cache-aside (hit → DTO; miss → `ShipmentTrackings` lookup → 404 or warm + respond) and `GET /api/tracking/{trackingNumber}/events` raw ordered timeline (no cache)
- `IDlqAdmin` / `RabbitDlqAdmin` — `CountAsync` via `QueueDeclarePassiveAsync(RabbitTopology.Dlq)`; `ReplayAsync` drains with `BasicGetAsync(autoAck: true)` and republishes bodies to `kargonet.shipments` / `shipment` with no properties (fresh retry budget); `GET /api/admin/dlq` + `POST /api/admin/dlq/replay`
- `Program.cs` full host: InMemory/Npgsql switch on `Database:Provider`/`Database:Name`, DI for cache + DLQ admin, `/health`, both endpoint groups; `internal partial class Program` kept, public `TestEntryPoint` marker added
- 9 new tests: 4 tracking endpoints (warm, cache-hit staleness, 404, ordered timeline), 2 DLQ endpoints, 3 `RabbitDlqAdmin` over a complete `FakeChannel : IChannel`

## Task Commits

1. **Task 1: Redis cache + lazy connections** - `b3d5599` (plan commit)
2. **Task 2: Tracking + DLQ endpoints, Program host** - `b3d5599` (plan commit)
3. **Task 3: Tests (TDD)** - `b3d5599` (plan commit)

## Files Created/Modified

- `src/Services/Tracking.Api/Tracking/TrackingDto.cs` — HTTP response record
- `src/Services/Tracking.Api/Tracking/RedisTrackingCache.cs` — frozen-interface Redis implementation
- `src/Services/Tracking.Api/Infra/LazyRedisConnection.cs` — lazy multiplexer
- `src/Services/Tracking.Api/Endpoints/{TrackingEndpoints,DlqEndpoints}.cs` — endpoint groups
- `src/Services/Tracking.Api/Dlq/{IDlqAdmin,RabbitDlqAdmin}.cs` — DLQ admin seam + implementation
- `src/BuildingBlocks/KargoNet.Messaging/Infra/LazyChannelFactory.cs` — new shared lazy channel infra
- `src/Services/Tracking.Api/Program.cs` — full host + `TestEntryPoint` marker
- `src/Services/Tracking.Api/Tracking.Api.csproj` — StackExchange.Redis 2.13.23
- `tests/KargoNet.Tracking.Tests/Api/{TrackingEndpointTests,DlqEndpointTests}.cs` — 6 tests
- `tests/KargoNet.Tracking.Tests/Dlq/RabbitDlqAdminTests.cs` — 3 tests + `FakeChannel : IChannel`

## Decisions Made

- StackExchange.Redis pinned at 2.13.23 (latest stable 2.x; 3.x excluded by the phase pin — a bare `dotnet add` resolves 3.4.35)
- `LazyChannelFactory` is new KargoNet.Messaging infra (not Phase 2 surface): `RabbitMq` config key, `virtual GetChannelAsync()`, non-sealed — the 04-03 `RabbitConsumerBroker` and hosted consumer build on it
- `Program` remains `internal partial` (CS0433 with Shipment.Api in the shared test assembly); `public partial class TestEntryPoint` is the `WebApplicationFactory<T>` marker
- DLQ replay publishes with no delivery properties on purpose: a replayed message gets a fresh `x-retry-count` budget

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking issue] `Program` kept `internal`, `TestEntryPoint` marker added**
- **Found during:** Task 2 (host wiring)
- **Issue:** The plan text describes a public `Program`; the Web-SDK synthesized public `Program` collides (CS0433) with Shipment.Api's public `Program` in the shared test assembly (constraint established in 04-01)
- **Fix:** `internal partial class Program { }` kept; added `public partial class TestEntryPoint { }` as the `WebApplicationFactory<T>` target
- **Files modified:** src/Services/Tracking.Api/Program.cs
- **Verification:** build 0/0
- **Committed in:** b3d5599

**2. [Rule 1 - Bug] API-shape fixes for StackExchange.Redis 2.13.23 and RabbitMQ.Client 7.2.2**
- **Found during:** Tasks 2-3 (compile + test runs)
- **Issue:** Several compiler-confirmed signature differences: 2.13.x exposes `RedisValue.IsNull` (no `IsNil` extension), has no CancellationToken overloads on `StringGetAsync`/`StringSetAsync`/`KeyDeleteAsync`, `ConnectAsync` returns `Task<ConnectionMultiplexer>` needing an upcast to `Task<IConnectionMultiplexer>`, and `JsonSerializer.Deserialize<T>` is ambiguous for a bare `RedisValue` (needs `value.ToString()`); 7.2.2's `QueueDeclarePassiveAsync` returns a single `QueueDeclareOk` (not an array), `BasicGetAsync` requires `autoAck` and returns `Task<BasicGetResult>`, and the generic `BasicPublishAsync<TProperties>` carries the constraint `IReadOnlyBasicProperties, IAmqpHeader`
- **Fix:** cache/admin code written against the verified signatures; the test `FakeChannel` implements the interface exactly (both `BasicPublishAsync` overloads with matching `where` clauses, both `QueueUnbindAsync` overloads, `BasicRejectAsync`, `Dispose`/`DisposeAsync`); `.NET 10` note: `IServiceCollection.RemoveAll` moved to `Microsoft.Extensions.DependencyInjection.Extensions`
- **Files modified:** src/Services/Tracking.Api/Tracking/RedisTrackingCache.cs, src/Services/Tracking.Api/Infra/LazyRedisConnection.cs, src/Services/Tracking.Api/Dlq/RabbitDlqAdmin.cs, tests/KargoNet.Tracking.Tests/Dlq/RabbitDlqAdminTests.cs, tests/KargoNet.Tracking.Tests/Api/{TrackingEndpointTests,DlqEndpointTests}.cs
- **Verification:** build 0/0; all 9 Task 3 tests pass
- **Committed in:** b3d5599

**3. [Rule 1 - Bug] Test disposed a DI-scoped context**
- **Found during:** Task 3 (`Second_get_is_served_from_cache_not_the_database` failed with ObjectDisposedException)
- **Issue:** The cache-staleness test mutated the DB row through a context from an already-disposed scope
- **Fix:** the mutation step now opens its own short-lived scope
- **Files modified:** tests/KargoNet.Tracking.Tests/Api/TrackingEndpointTests.cs
- **Verification:** all 9 Task 3 tests pass
- **Committed in:** b3d5599

---

**Total deviations:** 3 auto-fixed (1 plan-text adjustment, 2 API-shape/test bugs)

## Verification Evidence

```
$ ~/.dotnet/dotnet build /home/cek/projects/KargoNet/KargoNet.sln
0 Warning(s) 0 Error(s)

$ ~/.dotnet/dotnet test tests/KargoNet.Tracking.Tests/KargoNet.Tracking.Tests.csproj --filter "FullyQualifiedName~TrackingEndpointTests|FullyQualifiedName~DlqEndpointTests|FullyQualifiedName~RabbitDlqAdminTests"
Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9
```

---
*Phase: 04-tracking-api*
*Completed: 2026-11-20*

## Self-Check: PASSED
