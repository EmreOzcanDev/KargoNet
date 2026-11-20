using System.Net;
using System.Text.RegularExpressions;
using KargoNet.Portal.Tests.Fakes;
using Portal.Web.Services;

namespace KargoNet.Portal.Tests.Controllers;

public class ShipmentsControllerTests
{
    private static (PortalFactory Factory, HttpClient Client, FakePortalApiClient Fake) Create()
    {
        var fake = new FakePortalApiClient();
        var factory = new PortalFactory(fake);
        return (factory, factory.CreateClient(), fake);
    }

    private static async Task<string> GetAntiForgeryTokenAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/shipments/new");
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        Assert.True(match.Success, "antiforgery token not found on /shipments/new");
        return match.Groups[1].Value;
    }

    [Fact]
    public async Task New_RendersFormWithFieldsAndHint()
    {
        var (factory, client, _) = Create();
        using var _ = factory;

        var html = await client.GetStringAsync("/shipments/new");

        Assert.Contains("name=\"TrackingNumber\"", html);
        Assert.Contains("name=\"Description\"", html);
        Assert.Contains("name=\"Destination\"", html);
        Assert.Contains("Leave blank to auto-generate.", html);
        Assert.Contains("Create shipment", html);
    }

    [Fact]
    public async Task New_PostWithEmptyDestination_ReturnsBadRequestWithValidationMessage()
    {
        var (factory, client, _) = Create();
        using var _ = factory;

        var token = await GetAntiForgeryTokenAsync(client);

        var response = await client.PostAsync("/shipments/new", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["trackingNumber"] = "",
            ["description"] = "",
            ["destination"] = "",
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Destination is required.", html);
    }

    [Fact]
    public async Task New_PostValid_RedirectsToTrack()
    {
        var (factory, client, fake) = Create();
        using var _ = factory;
        var id = Guid.NewGuid();
        fake.CreatedShipment = new ShipmentDto(
            id, "KGN-777", "Parcels", "Ankara", "Created",
            new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero));
        fake.EntryFor("KGN-777").Tracking = new TrackingDto(
            "KGN-777", "Parcels", "Ankara", "Created",
            new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero));

        var token = await GetAntiForgeryTokenAsync(client);

        var response = await client.PostAsync("/shipments/new", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["trackingNumber"] = "KGN-777",
            ["description"] = "Parcels",
            ["destination"] = "Ankara",
            ["__RequestVerificationToken"] = token,
        }));

        // HttpClient follows the 302 → /track?number=KGN-777 and renders the found card.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("KGN-777", html);
        Assert.Contains("Parcels", html);
        Assert.Contains("Ankara", html);
        Assert.NotNull(fake.LastCreateRequest);
        Assert.Equal("Ankara", fake.LastCreateRequest!.destination);
    }

    [Fact]
    public async Task New_PostWhenApiFails_RendersErrorBanner()
    {
        var (factory, client, fake) = Create();
        using var _ = factory;
        fake.CreateException = new ApiRequestException("boom");

        var token = await GetAntiForgeryTokenAsync(client);

        var response = await client.PostAsync("/shipments/new", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["trackingNumber"] = "",
            ["description"] = "",
            ["destination"] = "Ankara",
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("reach the KargoNet service. Check your connection and try again.", html);
    }
}
