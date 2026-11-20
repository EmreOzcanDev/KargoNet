using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace KargoNet.Tracking.Api.Data;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can generate migrations with no
/// live database — the connection string is never opened during generation.
/// </summary>
public class TrackingDbContextFactory : IDesignTimeDbContextFactory<TrackingDbContext>
{
    public TrackingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TrackingDbContext>()
            .UseNpgsql("Host=localhost;Database=KargoNet_Tracking;Username=postgres;Password=postgres")
            .Options;

        return new TrackingDbContext(options);
    }
}
