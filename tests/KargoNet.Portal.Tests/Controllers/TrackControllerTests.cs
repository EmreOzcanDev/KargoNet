using System.Net;
using KargoNet.Portal.Tests.Fakes;
using Portal.Web.Services;

namespace KargoNet.Portal.Tests.Controllers;

public class TrackControllerTests
{
    private static (PortalFactory Factory, HttpClient Client, FakePortalApiClient Fake) Create(
        TrackingDto? tracking,
        IReadOnlyList<TrackingEventDto>? timeline = null)
    {
        var fake = new FakePortalApiClient();
        if (tracking is not null || timeline is not null)
        {
            var number = tracking?.trackingNumber ?? "KGN-123";
            var entry = fake.EntryFor(number);
            entry.Tracking = tracking;
            entry.Timeline = timeline;
        }

        var factory = new PortalFactory(fake);
        return (factory, factory.CreateClient(), fake);
    }

    private static TrackingDto SampleTracking(string status = "Delivered") => new(
        "KGN-123", "Gift box", "Berlin", status,
        new DateTimeOffset(2026, 11, 15, 9, 30, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 11, 18, 14, 5, 0, TimeSpan.Zero));

    [Fact]
    public async Task Track_Found_RendersShipmentCardAndTimeline()
    {
        var timeline = new[]
        {
            new TrackingEventDto(Guid.NewGuid(), "KGN-123", "shipment.created",
                """{"trackingNumber":"KGN-123","description":"Gift box","destination":"Berlin"}""",
                new DateTimeOffset(2026, 11, 15, 9, 30, 0, TimeSpan.Zero)),
            new TrackingEventDto(Guid.NewGuid(), "KGN-123", "shipment.status.changed",
                """{"trackingNumber":"KGN-123","status":"Delivered"}""",
                new DateTimeOffset(2026, 11, 18, 14, 5, 0, TimeSpan.Zero))
        };

        var (factory, client, _) = Create(SampleTracking(), timeline);
        using var _ = factory;

        var html = await client.GetStringAsync("/track?number=KGN-123");

        Assert.Contains("KGN-123", html);
        Assert.Contains("Gift box", html);
        Assert.Contains("Berlin", html);
        Assert.Contains("2026-11-15 09:30", html);
        Assert.Contains("2026-11-18 14:05", html);
        Assert.Contains("Shipment created", html);
        Assert.Contains("Status changed to Delivered", html);
        Assert.Contains("badge bg-success", html);
    }

    [Fact]
    public async Task Track_NoNumber_RendersEmptyState()
    {
        var (factory, client, _) = Create(null);
        using var _ = factory;

        var html = await client.GetStringAsync("/track");

        Assert.Contains("Enter a tracking number", html);
        Assert.Contains("Look up a shipment using the tracking number from your confirmation.", html);
    }

    [Fact]
    public async Task Track_UnknownNumber_RendersNotFound()
    {
        var (factory, client, _) = Create(null);
        using var _ = factory;

        var html = await client.GetStringAsync("/track?number=NOPE");

        Assert.Contains("No shipment found for NOPE", html);
        Assert.Contains("Check the tracking number and try again.", html);
        Assert.Contains("Back to home", html);
    }

    [Fact]
    public async Task Track_ApiFails_RendersErrorState()
    {
        var fake = new FakePortalApiClient();
        fake.EntryFor("BAD").TrackingException = new ApiRequestException("boom");
        var factory = new PortalFactory(fake);
        using var _ = factory;
        var client = factory.CreateClient();

        var response = await client.GetAsync("/track?number=BAD");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("alert-danger", html);
        Assert.Contains("reach the KargoNet service. Check your connection and try again.", html);
    }

    [Fact]
    public async Task Track_TimelineFails_RendersCardWithTimelineError()
    {
        var (factory, client, fake) = Create(SampleTracking("Created"));
        using var _ = factory;
        fake.EntryFor("KGN-123").TimelineException = new ApiRequestException("boom");

        var html = await client.GetStringAsync("/track?number=KGN-123");

        Assert.Contains("Gift box", html);
        Assert.Contains("Berlin", html);
        Assert.Contains("Timeline", html);
        Assert.Contains("reach the KargoNet service. Check your connection and try again.", html);
    }

    [Fact]
    public async Task Track_SingleCreatedEvent_RendersOneRow()
    {
        var timeline = new[]
        {
            new TrackingEventDto(Guid.NewGuid(), "ONE", "shipment.created",
                """{"trackingNumber":"ONE","description":"Laptop","destination":"Istanbul"}""",
                new DateTimeOffset(2026, 11, 20, 8, 15, 0, TimeSpan.Zero))
        };

        var tracking = new TrackingDto("ONE", "Laptop", "Istanbul", "Created",
            new DateTimeOffset(2026, 11, 20, 8, 15, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 20, 8, 15, 0, TimeSpan.Zero));

        var (factory, client, _) = Create(tracking, timeline);
        using var _ = factory;

        var html = await client.GetStringAsync("/track?number=ONE");

        Assert.Contains("Shipment created", html);
        Assert.Contains("Laptop / Istanbul", html);
        Assert.Contains("2026-11-20 08:15", html);
    }

    [Fact]
    public async Task Track_WeirdEvent_RendersRawEventTypeLabel()
    {
        var timeline = new[]
        {
            new TrackingEventDto(Guid.NewGuid(), "WEIRD", "custom.unknown",
                "not-json{",
                new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero))
        };

        var tracking = new TrackingDto("WEIRD", "Mystery", "Lisbon", "InTransit",
            new DateTimeOffset(2026, 11, 19, 7, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));

        var (factory, client, _) = Create(tracking, timeline);
        using var _ = factory;

        var response = await client.GetAsync("/track?number=WEIRD");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("custom.unknown", html);
        Assert.Contains("2026-11-20 09:00", html);
        Assert.DoesNotContain("No events yet", html);
    }
}
