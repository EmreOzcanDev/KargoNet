using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KargoNet.Tracking.Api.Dlq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KargoNet.Tracking.Tests.Api
{
    public class DlqEndpointTests
    {
        private readonly string _databaseName = Guid.NewGuid().ToString("N");
        private readonly DlqApiFactory _factory;

        public DlqEndpointTests()
            => _factory = new DlqApiFactory(_databaseName);

        [Fact]
        public async Task Get_admin_dlq_returns_the_pending_message_count()
        {
            var client = _factory.CreateClient();
            var response = await client.GetFromJsonAsync<JsonElement>("/api/admin/dlq");

            Assert.Equal(7, response.GetProperty("count").GetInt32());
        }

        [Fact]
        public async Task Post_admin_dlq_replay_returns_the_number_of_replayed_messages()
        {
            var client = _factory.CreateClient();
            var response = await client.PostAsync("/api/admin/dlq/replay", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(3, body.GetProperty("replayed").GetInt32());
        }

        /// <summary>
        /// Hosts the API with an <see cref="IDlqAdmin"/> fake returning fixed
        /// counts, so the endpoints are testable without a broker.
        /// </summary>
        private sealed class DlqApiFactory : WebApplicationFactory<TestEntryPoint>
        {
            private readonly string _databaseName;

            public DlqApiFactory(string databaseName)
                => _databaseName = databaseName;

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.UseEnvironment($"testing-{_databaseName}");
                builder.UseSetting("Database:Provider", "InMemory");
                builder.UseSetting("Database:Name", _databaseName);

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IDlqAdmin>();
                    services.AddSingleton<IDlqAdmin>(new FakeDlqAdmin());
                });
            }
        }

        private sealed class FakeDlqAdmin : IDlqAdmin
        {
            public Task<int> CountAsync() => Task.FromResult(7);
            public Task<int> ReplayAsync(CancellationToken ct) => Task.FromResult(3);
        }
    }
}
