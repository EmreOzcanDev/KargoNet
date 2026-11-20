using System.Security.Claims;
using KargoNet.Tracking.Api.Realtime;
using KargoNet.Tracking.Api.Tracking;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;

namespace KargoNet.Tracking.Tests.Realtime
{
    /// <summary>
    /// Plan 04-03: <see cref="TrackingHub"/> membership — joining puts the
    /// connection into its tracking-number group, leaving removes it.
    /// </summary>
    public class TrackingHubTests
    {
        [Fact]
        public async Task JoinTrackingGroup_adds_connection_to_the_tracking_number_group()
        {
            var groups = new FakeGroupManager();
            var hub = CreateHub(groups);

            await hub.JoinTrackingGroup("TRK-101");

            Assert.Equal(["conn-1"], groups.Membership("TRK-101"));
        }

        [Fact]
        public async Task LeaveTrackingGroup_removes_connection_from_the_tracking_number_group()
        {
            var groups = new FakeGroupManager();
            var hub = CreateHub(groups);

            await hub.JoinTrackingGroup("TRK-102");
            await hub.LeaveTrackingGroup("TRK-102");

            Assert.Empty(groups.Membership("TRK-102"));
        }

        private static TrackingHub CreateHub(FakeGroupManager groups)
        {
            var hub = new TrackingHub();
            hub.Context = new FixedCallerContext();
            hub.Groups = groups;
            return hub;
        }

        /// <summary>Minimal <see cref="HubCallerContext"/> pinned to one connection id.</summary>
        private sealed class FixedCallerContext : HubCallerContext
        {
            public override string ConnectionId => "conn-1";
            public override string? UserIdentifier => null;
            public override ClaimsPrincipal? User => null;
            public override IDictionary<object, object?> Items => new Dictionary<object, object?>();
            public override IFeatureCollection Features => throw new NotSupportedException();
            public override CancellationToken ConnectionAborted => default;
            public override void Abort() { }
        }

        private sealed class FakeGroupManager : IGroupManager
        {
            private readonly Dictionary<string, List<string>> _groups = new();

            public List<string> Membership(string group)
                => _groups.TryGetValue(group, out var members) ? [.. members] : [];

            public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            {
                if (!_groups.TryGetValue(groupName, out var members))
                    _groups[groupName] = members = [];
                if (!members.Contains(connectionId))
                    members.Add(connectionId);
                return Task.CompletedTask;
            }

            public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            {
                if (_groups.TryGetValue(groupName, out var members))
                    members.Remove(connectionId);
                return Task.CompletedTask;
            }
        }
    }
}
