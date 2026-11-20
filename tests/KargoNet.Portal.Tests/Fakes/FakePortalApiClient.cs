using Portal.Web.Services;

namespace KargoNet.Portal.Tests.Fakes;

public class FakePortalApiClient : IPortalApiClient
{
    public class FakeEntry
    {
        public TrackingDto? Tracking { get; set; }
        public IReadOnlyList<TrackingEventDto>? Timeline { get; set; }
        public Exception? TrackingException { get; set; }
        public Exception? TimelineException { get; set; }
    }

    public Dictionary<string, FakeEntry> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ShipmentDto? CreatedShipment { get; set; }
    public Exception? CreateException { get; set; }
    public CreateShipmentRequestDto? LastCreateRequest { get; private set; }

    public FakeEntry EntryFor(string number)
    {
        if (!Entries.TryGetValue(number, out var entry))
        {
            entry = new FakeEntry();
            Entries[number] = entry;
        }
        return entry;
    }

    public Task<TrackingDto?> GetTrackingAsync(string number, CancellationToken ct = default)
    {
        var entry = Entries.TryGetValue(number, out var e) ? e : null;
        if (entry?.TrackingException is { } ex)
        {
            return Task.FromException<TrackingDto?>(ex);
        }
        return Task.FromResult(entry?.Tracking);
    }

    public Task<IReadOnlyList<TrackingEventDto>> GetTimelineAsync(string number, CancellationToken ct = default)
    {
        var entry = Entries.TryGetValue(number, out var e) ? e : null;
        if (entry?.TimelineException is { } ex)
        {
            return Task.FromException<IReadOnlyList<TrackingEventDto>>(ex);
        }
        return Task.FromResult(entry?.Timeline ?? (IReadOnlyList<TrackingEventDto>)Array.Empty<TrackingEventDto>());
    }

    public Task<ShipmentDto> CreateShipmentAsync(CreateShipmentRequestDto request, CancellationToken ct = default)
    {
        LastCreateRequest = request;
        if (CreateException is { } ex)
        {
            return Task.FromException<ShipmentDto>(ex);
        }
        if (CreatedShipment is { } dto)
        {
            return Task.FromResult(dto);
        }
        return Task.FromException<ShipmentDto>(new InvalidOperationException("FakePortalApiClient has no CreatedShipment configured."));
    }
}
