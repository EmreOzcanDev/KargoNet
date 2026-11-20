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
}
