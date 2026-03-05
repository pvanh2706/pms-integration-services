using Serilog;
using PmsIntegration.Application.DI;
using PmsIntegration.Infrastructure.DI;
using PmsIntegration.Infrastructure.Logging;
using PmsIntegration.Host.Middleware;
using PmsIntegration.Host.Options;
using PmsIntegration.Host.Background;
using PmsIntegration.Host.Providers;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ────────────────────────────────────────────────────────────────
Log.Logger = SerilogElasticSetup
    .Configure(new LoggerConfiguration(), builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();

// ── Options ────────────────────────────────────────────────────────────────
builder.Services.Configure<PmsSecurityOptions>(
    builder.Configuration.GetSection("PmsSecurity"));

// ── Layers ────────────────────────────────────────────────────────────────
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// ── Providers ─────────────────────────────────────────────────────────────
// Each IPmsProvider registers itself; Host only calls AddProviders().
// To add a new provider: create Providers/<Name>/ and call AddProviders() — done.
builder.Services.AddProviders(builder.Configuration);

// ── Consumers ─────────────────────────────────────────────────────────────
// ProviderConsumerOrchestrator discovers all registered IPmsProvider keys and
// starts one queue consumer per provider automatically.
// No changes here when adding a new provider — just register it in AddProviders().
builder.Services.AddSingleton<IHostedService, ProviderConsumerOrchestrator>();

// ── Health checks ────────────────────────────────────────────────────────
// BUG-5 FIX: expose /health outside /api/pms so load-balancer probes are
// never blocked by PmsTokenMiddleware.
builder.Services.AddHealthChecks();

// ── MVC ───────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// ── Startup validation ───────────────────────────────────────────────────
// BUG-3 FIX: fail fast at startup if the token is blank (all envs) or still the
// shipped default (non-Development), so misconfigured deployments surface immediately.
var securityOpts = app.Services.GetRequiredService<IOptions<PmsSecurityOptions>>().Value;
if (string.IsNullOrWhiteSpace(securityOpts.FixedToken))
    throw new InvalidOperationException(
        "PmsSecurity:FixedToken is not configured. Set a strong random value in appsettings or environment variables.");
if (!app.Environment.IsDevelopment() && securityOpts.FixedToken == "change-me-in-production")
    throw new InvalidOperationException(
        "PmsSecurity:FixedToken is still the default placeholder. Replace it with a strong random value before starting.");

// ── Middleware ─────────────────────────────────────────────────────────────
// /health sits outside /api/pms, so PmsTokenMiddleware skips it automatically.
// Kubernetes/Docker liveness and readiness probes should point to /health.
app.MapHealthChecks("/health");

app.UseMiddleware<PmsTokenMiddleware>();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
