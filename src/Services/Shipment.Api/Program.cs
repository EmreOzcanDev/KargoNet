using KargoNet.Contracts.Models;
using KargoNet.Messaging.Publishing;
using KargoNet.Shipment.Api.Data;
using KargoNet.Shipment.Api.Endpoints;
using KargoNet.Shipment.Api.Publishing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

var useInMemory = builder.Configuration["Database:Provider"] == "InMemory";
var inMemoryName = builder.Configuration["Database:Name"] ?? "ShipmentDb";

builder.Services.AddDbContext<ShipmentDbContext>(options =>
{
    if (useInMemory)
        options.UseInMemoryDatabase(inMemoryName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
    else
        options.UseNpgsql(builder.Configuration.GetConnectionString("ShipmentDb"));
});

builder.Services.AddSingleton<IOutboxChannel, LazyOutboxChannelFactory>();
builder.Services.AddSingleton<MessagePublisher>();
builder.Services.AddHostedService<OutboxPublisherService>();

var app = builder.Build();

if (!useInMemory)
{
    for (var attempt = 1; attempt <= 10; attempt++)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<ShipmentDbContext>()
                .Database.MigrateAsync();
            break;
        }
        catch (Exception) when (attempt < 10)
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    shipmentStatuses = Enum.GetNames<ShipmentStatus>(),
}));

app.MapShipmentEndpoints();

app.Run();

public partial class Program { }
