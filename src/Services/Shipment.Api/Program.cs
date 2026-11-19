using KargoNet.Contracts.Models;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    shipmentStatuses = Enum.GetNames<ShipmentStatus>(),
}));

app.Run();
