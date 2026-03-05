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

// ── MVC ───────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// ── Middleware ─────────────────────────────────────────────────────────────
app.UseMiddleware<PmsTokenMiddleware>();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
