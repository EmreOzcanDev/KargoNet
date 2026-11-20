var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Portal API client against the frozen Shipment + Tracking services.
builder.Services.AddSingleton<Portal.Web.Services.IPortalApiClient>(_ => new Portal.Web.Services.PortalApiClient(
    new Uri(builder.Configuration["ShipmentApiUrl"]!),
    new Uri(builder.Configuration["TrackingApiUrl"]!)));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
