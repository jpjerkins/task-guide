using Microsoft.Extensions.DependencyInjection.Extensions;
using TaskGuide.Api.Endpoints;
using TaskGuide.Application.Ports;
using TaskGuide.Domain.Common;
using TaskGuide.Domain.Dimensions;
using TaskGuide.Domain.Schedule;
using TaskGuide.Infrastructure.BackgroundServices;
using TaskGuide.Infrastructure.Configuration;
using TaskGuide.Infrastructure.Ids;
using TaskGuide.Infrastructure.Storage;

// One container: API + SPA + the ~30s tick loop (#5, #6). Host-mode port 8007, tailnet-only,
// TLS via Tailscale Serve, no auth — single user, gated at the network layer.
var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://0.0.0.0:8007"); // host-mode, tailnet-only, TLS terminated by Tailscale Serve

// Pushover's two secrets arrive here, not through compose's `env_file:`. vault-t2 serves this
// path over FUSE to UID 50013 alone and denies even root, so the docker client cannot read it at
// deploy time — the process must read it itself, at runtime, as its own UID (#51). Optional:
// there is no FUSE mount outside the container, and PushoverClient already no-ops unconfigured.
builder.Configuration.AddEnvFile(
    builder.Configuration["Secrets:EnvFile"] ?? "/run/vault-t2-fs/envfiles/task-guide",
    optional: true);

// builder.Host.UseSerilog(...);            // rolling daily file sink, retention count. Receipts land here.
// builder.Services.AddTaskGuideDomain();   // Dimension registry, rules, clock

// Configurable so tests (and a local run) aren't forced to write to a root path; "/data" is only
// the container's bind-mount default.
var dataDir = builder.Configuration["Storage:DataDir"] ?? "/data";

// ADR-0009: plan (may refuse, cannot write) → apply (cannot refuse) → open the runtime store.
// Awaited here, before Build(), so the *completed* store is what gets registered — no temporary
// service provider, no holder filled in later, and the bootstrap view is never registered at all.
var store = await StartupBootstrap.BootstrapAndOpenStoreAsync(
    dataDir,
    KnownDimensions.Default,
    StoreMigrations.Ordered,
    new UlidIdMinter(),
    TimeProvider.System,
    signalRegistryCollision: (_, _) => Task.CompletedTask,   // nothing outbound wired yet
    CancellationToken.None);

builder.Services.AddSingleton<IStore>(store);
builder.Services.AddSingleton<IStoreReader>(store);
builder.Services.AddSingleton(KnownDimensions.Default);
// AddPushover below also TryAddSingleton(TimeProvider.System); kept here instead because this is
// where the process-wide clock is first needed (the bootstrap call above), and a reader shouldn't
// have to look inside AddPushover to find where the clock they depend on comes from.
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IIdMinter, UlidIdMinter>();
builder.Services.AddSingleton<IDayShapeReader, DayShapeReader>();
builder.Services.AddPushover(builder.Configuration);
builder.Services.AddHealthReporter(dataDir);
builder.Services.AddHostedService<TickLoop>();
builder.Services.AddOpenApi();               // TS types are generated from this output (openapi-typescript)

var app = builder.Build();

app.MapOpenApi();
app.UseDefaultFiles();
app.UseStaticFiles();                        // the React SPA, built into wwwroot/

var api = app.MapGroup("/api");
api.MapTaskEndpoints();
api.MapCaptureEndpoints();
api.MapRightNowEndpoints();
api.MapReminderEndpoints();
api.MapDayTemplateEndpoints();
api.MapWindowEndpoints();
api.MapPatternEndpoints();
api.MapOverrideEndpoints();
api.MapDayEndpoints();
api.MapEventEndpoints();
api.MapDimensionEndpoints();

app.MapHealthEndpoints();
app.MapFallbackToFile("index.html");         // the SPA owns client-side routing

app.Run();

// Top-level statements generate an `internal` Program class; WebApplicationFactory<Program> in
// TaskGuide.Api.Tests needs it visible across the assembly boundary.
public partial class Program;
