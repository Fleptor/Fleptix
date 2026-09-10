using Fleptix.Core.Interfaces;
using Fleptix.Observer.Hubs;
using Fleptix.Observer.Services;

var builder = WebApplication.CreateBuilder(args);

// Add Razor Pages and API Controllers
builder.Services.AddRazorPages();
builder.Services.AddControllers();

// Add SignalR for live metrics and status streaming
builder.Services.AddSignalR();

// Register Licensing and Feature Services
builder.Services.AddSingleton<ILicenseService, LicenseService>();

// Register Time Machine Snapshot & Retention Services (Dynamic plugin with Community fallback)
builder.Services.AddFleptixTimeMachine(builder.Configuration);

// Register Container Services
builder.Services.AddSingleton<DockerContainerService>();
builder.Services.AddSingleton<DemoContainerService>();
builder.Services.AddSingleton<ContainerServiceManager>();
builder.Services.AddSingleton<IContainerService>(sp => sp.GetRequiredService<ContainerServiceManager>());


// Register real-time telemetry background publisher
builder.Services.AddHostedService<TelemetryBackgroundService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();
app.MapHub<ContainerHub>("/hubs/containers");

app.Run();
