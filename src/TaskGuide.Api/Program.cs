using TaskGuide.Api.Endpoints;
using TaskGuide.Domain.Common;
using TaskGuide.Infrastructure.BackgroundServices;
using TaskGuide.Infrastructure.Ids;

// One container: API + SPA + the ~30s tick loop (#5, #6). Host-mode port 8007, tailnet-only,
// TLS via Tailscale Serve, no auth — single user, gated at the network layer.
var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://0.0.0.0:8007"); // host-mode, tailnet-only, TLS terminated by Tailscale Serve

// builder.Host.UseSerilog(...);            // rolling daily file sink, retention count. Receipts land here.
// builder.Services.AddTaskGuideDomain();   // Dimension registry, rules, clock

// Configurable so tests (and a local run) aren't forced to write to a root path; "/data" is only
// the container's bind-mount default.
var dataDir = builder.Configuration["Storage:DataDir"] ?? "/data";
builder.Services.AddJsonStore(dataDir);      // memory-authoritative, one global write lock
builder.Services.AddSingleton<IIdMinter, UlidIdMinter>();
builder.Services.AddPushover(builder.Configuration);
builder.Services.AddHealthReporter(dataDir);
builder.Services.AddHostedService<TickLoop>();
builder.Services.AddOpenApi();               // TS types are generated from this output (openapi-typescript)

var app = builder.Build();

// assert → snapshot → migrate → sweep → serve. A registry collision refuses to start.
// await app.Services.GetRequiredService<IStartupSequence>().RunAsync();

app.MapOpenApi();
app.UseDefaultFiles();
app.UseStaticFiles();                        // the React SPA, built into wwwroot/

var api = app.MapGroup("/api");
api.MapTaskEndpoints();
api.MapCaptureEndpoints();
api.MapRightNowEndpoints();
api.MapReminderEndpoints();
api.MapScheduleEndpoints();
api.MapEventEndpoints();
api.MapDimensionEndpoints();

app.MapHealthEndpoints();
app.MapFallbackToFile("index.html");         // the SPA owns client-side routing

app.Run();

// Top-level statements generate an `internal` Program class; WebApplicationFactory<Program> in
// TaskGuide.Api.Tests needs it visible across the assembly boundary.
public partial class Program;
