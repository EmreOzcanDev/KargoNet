using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace KargoNet.Shipment.Api.Data;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can generate migrations with no
/// live database — the connection string is never opened during generation.
/// </summary>
public class ShipmentDbContextFactory : IDesignTimeDbContextFactory<ShipmentDbContext>
{
    public ShipmentDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("ShipmentDb")
            ?? "Server=localhost;Database=shipment_dummy;Username=dummy;Password=dummy";

        var options = new DbContextOptionsBuilder<ShipmentDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ShipmentDbContext(options);
    }
}
