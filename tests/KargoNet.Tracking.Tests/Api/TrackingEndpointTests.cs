using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KargoNet.Tracking.Api.Data;
using KargoNet.Tracking.Api.Entities;
using KargoNet.Tracking.Api.Tracking;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace KargoNet.Tracking.Tests.Api
{
    /// <summary>
    /// Hosts the Tracking API against an isolated InMemory database and
    /// substitutes the real (Redis-backed) <see cref="ITrackingCache"/> with a
    /// recording fake, so cache-aside behavior is observable without Redis.
    /// </summary>
    public class TrackingApiFactory : WebApplicationFactory<TestEntryPoint>
    {
        private readonly string _databaseName;

        public TrackingApiFactory(string databaseName)
            => _databaseName = databaseName;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Unique environment per database name: the factory host cache is
            // keyed by environment, so each test gets an isolated host.
            builder.UseEnvironment($"testing-{_databaseName}");

            builder.UseSetting("Database:Provider", "InMemory");
            builder.UseSetting("Database:Name", _databaseName);

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITrackingCache>();
                services.AddSingleton<ITrackingCache>(new FakeTrackingCache());
            });
        }
    }

    /// <summary>In-memory <see cref="ITrackingCache"/> recording every call.</summary>
    public sealed class FakeTrackingCache : ITrackingCache
    {
        private readonly Dictionary<string, TrackingUpdatedDto> _store = new();

        public int GetCount { get; private set; }
        public int SetCount { get; private set; }
        public int DeleteCount { get; private set; }

        public IReadOnlyDictionary<string, TrackingUpdatedDto> Store => _store;

        public Task<TrackingUpdatedDto?> GetAsync(string trackingNumber, CancellationToken ct)
        {
            GetCount++;
            return Task.FromResult(_store.TryGetValue(trackingNumber, out var dto) ? dto : null);
        }

        public Task SetAsync(TrackingUpdatedDto dto, TimeSpan ttl, CancellationToken ct)
        {
            SetCount++;
            _store[dto.trackingNumber] = dto;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string trackingNumber, CancellationToken ct)
        {
            DeleteCount++;
            _store.Remove(trackingNumber);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// An <see cref="ITrackingCache"/> that always fails, simulating an
    /// unreachable Redis for the degrade-to-EF test.
    /// </summary>
    public sealed class FailingTrackingCache : ITrackingCache
    {
        private static readonly Exception Failure =
            new InvalidOperationException("simulated Redis outage");

        public Task<TrackingUpdatedDto?> GetAsync(string trackingNumber, CancellationToken ct)
            => Task.FromException<TrackingUpdatedDto?>(Failure);

        public Task SetAsync(TrackingUpdatedDto dto, TimeSpan ttl, CancellationToken ct)
            => Task.FromException(Failure);

        public Task DeleteAsync(string trackingNumber, CancellationToken ct)
            => Task.FromException(Failure);
    }

    public class TrackingEndpointTests
    {
        private static readonly DateTimeOffset CreatedAt = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset UpdatedAt = new(2026, 11, 2, 10, 30, 0, TimeSpan.Zero);

        private readonly string _databaseName = Guid.NewGuid().ToString("N");
        private readonly TrackingApiFactory _factory;
        private readonly FakeTrackingCache _cache;

        public TrackingEndpointTests()
        {
            _factory = new TrackingApiFactory(_databaseName);
            _cache = (FakeTrackingCache)_factory.Services.GetRequiredService<ITrackingCache>();
        }

        private async Task SeedAsync(params object[] rows)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TrackingDbContext>();
            db.ChangeTracker.Clear();
            db.AddRange(rows);
            await db.SaveChangesAsync();
        }

        [Fact]
        public async Task Get_known_tracking_number_returns_200_dto_and_warms_the_cache()
        {
            await SeedAsync(new ShipmentTracking
            {
                TrackingNumber = "TRK-1",
                Description = "Electronics",
                Destination = "Istanbul",
                Status = "InTransit",
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt,
            });

            var client = _factory.CreateClient();
            var response = await client.GetAsync("/api/tracking/TRK-1");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var dto = await response.Content.ReadFromJsonAsync<TrackingDto>();
            Assert.Equal("TRK-1", dto!.trackingNumber);
            Assert.Equal("Electronics", dto.description);
            Assert.Equal("Istanbul", dto.destination);
            Assert.Equal("InTransit", dto.status);
            Assert.Equal(CreatedAt, dto.createdAt);
            Assert.Equal(UpdatedAt, dto.updatedAt);

            // The miss was warmed into the cache with the same row.
            Assert.Equal(1, _cache.SetCount);
            Assert.Equal("Electronics", _cache.Store["TRK-1"].description);
        }

        [Fact]
        public async Task Second_get_is_served_from_cache_not_the_database()
        {
            await SeedAsync(new ShipmentTracking
            {
                TrackingNumber = "TRK-2",
                Description = "Original",
                Destination = "Ankara",
                Status = "Created",
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt,
            });

            var client = _factory.CreateClient();
            var first = await client.GetFromJsonAsync<TrackingDto>("/api/tracking/TRK-2");
            Assert.Equal("Original", first!.description);

            // Mutate the row AFTER the cache warm: a cache hit must return the
            // stale (cached) value, proving the second GET skipped EF.
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<TrackingDbContext>();
                var row = await db.ShipmentTrackings.SingleAsync(t => t.TrackingNumber == "TRK-2");
                row.Description = "Mutated";
                await db.SaveChangesAsync();
            }
            var second = await client.GetFromJsonAsync<TrackingDto>("/api/tracking/TRK-2");
            Assert.Equal("Original", second!.description);
            Assert.Equal(2, _cache.GetCount);
            Assert.Equal(1, _cache.SetCount); // no second warm
        }

        [Fact]
        public async Task Get_unknown_tracking_number_returns_404()
        {
            await SeedAsync();

            var client = _factory.CreateClient();
            var response = await client.GetAsync("/api/tracking/NOPE");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(0, _cache.SetCount);
        }

        [Fact]
        public async Task Get_degrades_to_EF_when_the_cache_fails()
        {
            var databaseName = Guid.NewGuid().ToString("N");
            var factory = new FailingCacheApiFactory(databaseName);

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<TrackingDbContext>();
                db.AddRange(new ShipmentTracking
                {
                    TrackingNumber = "TRK-FAIL",
                    Description = "Fallback",
                    Destination = "Izmir",
                    Status = "Created",
                    CreatedAt = CreatedAt,
                    UpdatedAt = UpdatedAt,
                });
                await db.SaveChangesAsync();
            }

            var client = factory.CreateClient();
            var response = await client.GetAsync("/api/tracking/TRK-FAIL");

            // Both cache calls (read + warm) throw; the request must still
            // succeed from the EF row.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var dto = await response.Content.ReadFromJsonAsync<TrackingDto>();
            Assert.Equal("TRK-FAIL", dto!.trackingNumber);
            Assert.Equal("Fallback", dto.description);
            Assert.Equal("Izmir", dto.destination);
        }

        [Fact]
        public async Task Events_endpoint_returns_timeline_ordered_by_occurred_at()
        {
            // Seed OUT of order: the API must return them in OccurredAt order.
            await SeedAsync(
                new TrackingEvent
                {
                    EventId = Guid.NewGuid(),
                    TrackingNumber = "TRK-3",
                    EventType = "shipment.status.changed",
                    PayloadJson = "{}",
                    OccurredAt = UpdatedAt,
                },
                new TrackingEvent
                {
                    EventId = Guid.NewGuid(),
                    TrackingNumber = "TRK-3",
                    EventType = "shipment.created",
                    PayloadJson = "{}",
                    OccurredAt = CreatedAt,
                },
                new TrackingEvent
                {
                    EventId = Guid.NewGuid(),
                    TrackingNumber = "TRK-OTHER",
                    EventType = "shipment.created",
                    PayloadJson = "{}",
                    OccurredAt = CreatedAt,
                });

            var client = _factory.CreateClient();
            var response = await client.GetFromJsonAsync<JsonElement>("/api/tracking/TRK-3/events");

            var timeline = response.EnumerateArray().ToList();
            Assert.Equal(2, timeline.Count); // TRK-OTHER's row is excluded
            Assert.Equal("shipment.created", timeline[0].GetProperty("eventType").GetString());
            Assert.Equal("shipment.status.changed", timeline[1].GetProperty("eventType").GetString());
            Assert.True(timeline[0].GetProperty("occurredAt").GetDateTimeOffset()
                < timeline[1].GetProperty("occurredAt").GetDateTimeOffset());
        }

        /// <summary>
        /// The <see cref="TrackingApiFactory"/> host with an always-failing
        /// <see cref="ITrackingCache"/>, proving the read path degrades to EF.
        /// </summary>
        private sealed class FailingCacheApiFactory : WebApplicationFactory<TestEntryPoint>
        {
            private readonly string _databaseName;

            public FailingCacheApiFactory(string databaseName)
                => _databaseName = databaseName;

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.UseEnvironment($"testing-{_databaseName}");
                builder.UseSetting("Database:Provider", "InMemory");
                builder.UseSetting("Database:Name", _databaseName);

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ITrackingCache>();
                    services.AddSingleton<ITrackingCache>(new FailingTrackingCache());
                });
            }
        }
    }
}
