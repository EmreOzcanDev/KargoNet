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

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    shipmentStatuses = Enum.GetNames<ShipmentStatus>(),
}));

app.MapShipmentEndpoints();

app.Run();

public partial class Program { }
