using KargoNet.Portal.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Portal.Web.Services;

namespace KargoNet.Portal.Tests;

public class PortalFactory : WebApplicationFactory<Program>
{
    private readonly FakePortalApiClient _fake;

    public PortalFactory(FakePortalApiClient fake)
    {
        _fake = fake;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPortalApiClient>();
            services.AddSingleton<IPortalApiClient>(_ => _fake);
        });
    }
}
