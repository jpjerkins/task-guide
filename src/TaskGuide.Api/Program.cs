using TaskGuide.Api.Endpoints;

// One container: API + SPA + the ~30s tick loop (#5, #6). Host-mode port 8007, tailnet-only,
// TLS via Tailscale Serve, no auth — single user, gated at the network layer.
var builder = WebApplication.CreateBuilder(args);

// builder.Host.UseSerilog(...);            // rolling daily file sink, retention count. Receipts land here.
// builder.Services.AddTaskGuideDomain();   // Dimension registry, rules, clock
// builder.Services.AddJsonStore("/data");  // memory-authoritative, one global write lock
// builder.Services.AddHostedService<TickLoop>();
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
