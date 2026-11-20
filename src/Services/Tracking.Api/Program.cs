using KargoNet.Tracking.Api.Consuming;
using KargoNet.Tracking.Api.Data;
using KargoNet.Tracking.Api.Dlq;
using KargoNet.Tracking.Api.Endpoints;
using KargoNet.Tracking.Api.Infra;
using KargoNet.Tracking.Api.Realtime;
using KargoNet.Tracking.Api.Tracking;
using KargoNet.Messaging.Consuming;
using KargoNet.Messaging.Infra;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

var useInMemory = builder.Configuration["Database:Provider"] == "InMemory";
var inMemoryName = builder.Configuration["Database:Name"] ?? "TrackingDb";

builder.Services.AddDbContext<TrackingDbContext>(options =>
{
    if (useInMemory)
        options.UseInMemoryDatabase(inMemoryName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
    else
        options.UseNpgsql(builder.Configuration.GetConnectionString("TrackingDb"));
});

builder.Services.AddSingleton<LazyRedisConnection>();
builder.Services.AddScoped<ITrackingCache, RedisTrackingCache>();
builder.Services.AddSingleton<LazyChannelFactory>();
builder.Services.AddSingleton<RabbitConsumerBroker>();
builder.Services.AddSingleton<IConsumerBroker>(sp => sp.GetRequiredService<RabbitConsumerBroker>());
builder.Services.AddScoped<IDlqAdmin, RabbitDlqAdmin>();
builder.Services.AddSignalR();
builder.Services.AddScoped<IShippingUpdateBroadcaster, SignalRShipmentUpdateBroadcaster>();
builder.Services.AddHostedService<ShipmentEventsConsumer>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
}));

app.MapHub<TrackingHub>("/hubs/tracking");
app.MapTrackingEndpoints();
app.MapDlqEndpoints();

app.Run();

// Kept internal (not public) on purpose: a public synthesized `Program` would
// collide (CS0433) with Shipment.Api's public `Program` in the shared test
// assembly. TestEntryPoint below is the public marker for
// WebApplicationFactory<T>.
internal partial class Program { }

public partial class TestEntryPoint { }
