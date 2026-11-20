using KargoNet.Tracking.Api.Realtime;
using KargoNet.Tracking.Api.Tracking;
using Microsoft.AspNetCore.SignalR;

namespace KargoNet.Tracking.Tests.Realtime
{
    /// <summary>
    /// Plan 04-03: <see cref="SignalRShipmentUpdateBroadcaster"/> sends the
    /// <c>ShipmentUpdated</c> payload to every connected client AND to the
    /// per-tracking-number group so the tracking page hears the update.
    /// </summary>
    public class SignalRShipmentUpdateBroadcasterTests
    {
        [Fact]
        public async Task BroadcastAsync_sends_ShipmentUpdated_to_all_clients_and_the_tracking_group()
        {
            var hubContext = new FakeHubContext();
            var broadcaster = new SignalRShipmentUpdateBroadcaster(hubContext);
            var dto = new TrackingUpdatedDto(
                "TRK-42", "Fragile", "Izmir", "PickedUp",
                new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 2, 9, 30, 0, TimeSpan.Zero));

            await broadcaster.BroadcastAsync(dto, CancellationToken.None);

            var clients = hubContext.FakeClients;
            var toAll = clients.Sends.Single(s => s.target == "all");
            Assert.Equal("ShipmentUpdated", toAll.method);
            Assert.Same(dto, toAll.arg);

            var toGroup = clients.Sends.Single(s => s.target == "TRK-42");
            Assert.Equal("ShipmentUpdated", toGroup.method);
            Assert.Same(dto, toGroup.arg);
        }

        private sealed class FakeHubContext : IHubContext<TrackingHub>
        {
            public FakeHubClients FakeClients { get; } = new();
            public IHubClients Clients => FakeClients;
            public IGroupManager Groups { get; } = null!;

            public Task OnDisconnectedAsync(HubConnectionContext? context, CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public Task OnConnectedAsync(HubConnectionContext context)
                => Task.CompletedTask;

            public Task SendAsync(string method, object? arg, CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public Task SendCoreAsync(HubConnectionContext hubContext, string method, object?[] args, CancellationToken cancellationToken = default)
                => Task.CompletedTask;
        }

        private sealed class FakeHubClients : IHubClients
        {
            public List<(string target, string method, object? arg)> Sends { get; } = [];

            public IClientProxy All => new FakeProxy(this, "all");

            public IClientProxy AllExcept(IReadOnlyList<string> clientIds)
                => new FakeProxy(this, "all-except");

            public IClientProxy Client(string clientId)
                => new FakeProxy(this, clientId);

            public IClientProxy Clients(IReadOnlyList<string> clientIds)
                => new FakeProxy(this, string.Join(",", clientIds));

            public IClientProxy Group(string groupName)
                => new FakeProxy(this, groupName);

            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> clientIds)
                => new FakeProxy(this, groupName + "-except");

            public IClientProxy Groups(IReadOnlyList<string> groupNames)
                => new FakeProxy(this, string.Join(",", groupNames));

            public IClientProxy User(string userId)
                => new FakeProxy(this, "user:" + userId);

            public IClientProxy Users(IReadOnlyList<string> userIds)
                => new FakeProxy(this, "users:" + string.Join(",", userIds));

            private sealed class FakeProxy : IClientProxy
            {
                private readonly FakeHubClients _clients;
                private readonly string _target;

                public FakeProxy(FakeHubClients clients, string target)
                {
                    _clients = clients;
                    _target = target;
                }
                public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
                {
                    _clients.Sends.Add((_target, method, args.Length > 0 ? args[0] : null));
                    return Task.CompletedTask;
                }

                public Task<TResult> InvokeCoreAsync<TResult>(string method, object?[] args, CancellationToken cancellationToken = default)
                    => Task.FromResult(default(TResult)!);
            }
        }
    }
}
