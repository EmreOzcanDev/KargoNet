using KargoNet.Portal.Tests.Fakes;

namespace KargoNet.Portal.Tests.Controllers;

public class HomeControllerTests
{
    [Fact]
    public async Task Index_RendersLookupCard()
    {
        var fake = new FakePortalApiClient();
        var factory = new PortalFactory(fake);
        using var _ = factory;
        var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Track your shipment", html);
        Assert.Contains("action=\"", html);
        Assert.Contains("name=\"number\"", html);
        Assert.Contains("Track shipment", html);
        Assert.Contains("Create shipment", html);
        Assert.Contains("KargoNet", html);
    }
}
