# PMS Integration Service

An integration gateway that connects a **Property Management System (PMS)** to multiple third-party **provider APIs** (Tiger, Opera, Fake) via RabbitMQ.

The service receives HTTP events from the PMS, fans each event out to per-provider RabbitMQ queues, and processes each queue independently — with retry and dead-letter handling built in.

---

## Table of Contents

1. [Quick Start](#quick-start)
2. [Running the Host](#running-the-host)
3. [Minimal Configuration](#minimal-configuration)
4. [Project Layout](#project-layout)
5. [Further Reading](#further-reading)

---

## Quick Start

### Prerequisites

| Tool | Minimum version |
|------|----------------|
| .NET SDK | 10.0 |
| RabbitMQ | 3.12+ |
| Elasticsearch | 6.8.23 (Kibana 6.8.23) |

### 1 — Clone and restore

```bash
git clone <repo-url>
cd pms-integration-service
dotnet restore PmsIntegration.sln
```

### 2 — Start RabbitMQ (Docker)

```bash
docker run -d --name rabbitmq \
  -p 5672:5672 -p 15672:15672 \
  rabbitmq:3-management
```

Management UI → http://localhost:15672 (guest / guest)

### 3 — Set the security token

Open `src/PmsIntegration.Host/appsettings.Development.json` and set:

```json
{
  "PmsSecurity": {
    "FixedToken": "my-local-dev-token"
  }
}
```

### 4 — Build and run

```bash
dotnet build PmsIntegration.sln
dotnet run --project src/PmsIntegration.Host/PmsIntegration.Host.csproj
```

The API is available at `https://localhost:<port>`.  
OpenAPI is exposed at `/openapi` in the Development environment.

### 5 — Send a test event

```bash
curl -X POST https://localhost:<port>/api/pms/events \
  -H "Content-Type: application/json" \
  -H "X-PMS-TOKEN: my-local-dev-token" \
  -d '{
    "hotelId": "H001",
    "eventId": "EVT-001",
    "eventType": "Checkin",
    "providers": ["FAKE"],
    "data": { "guestName": "Jane Doe" }
  }'
```

Expected response:

```json
HTTP 202 Accepted
X-Correlation-Id: <guid>

{ "status": "accepted", "correlationId": "<guid>" }
```

---

## Running the Host

### As a Console / HTTP API (default)

```bash
dotnet run --project src/PmsIntegration.Host/PmsIntegration.Host.csproj \
  --environment Production
```

The process runs Kestrel and also starts `ProviderConsumerOrchestrator` as a hosted background service. Both the API and consumers run in the same process.

### As a Windows Service

1. Publish a self-contained executable:

```powershell
dotnet publish src\PmsIntegration.Host\PmsIntegration.Host.csproj `
  -c Release -r win-x64 --self-contained true `
  -o C:\services\pms-integration
```

2. Install with `sc.exe`:

```powershell
sc.exe create PmsIntegrationService `
  binPath= "C:\services\pms-integration\PmsIntegration.Host.exe" `
  start= auto
sc.exe start PmsIntegrationService
```

> **Note:** To enable the Windows Service host, `Program.cs` must call `builder.Host.UseWindowsService()` before `builder.Build()`. Verify this is present before deploying as a service; add it if the project was generated without it.

3. Logs are written to the Elasticsearch index configured in `appsettings.json`. Windows Event Log is **not** used by default.

### Environment selection

Pass the environment name via the standard `ASPNETCORE_ENVIRONMENT` environment variable or the `--environment` CLI flag. `appsettings.{Environment}.json` is merged on top of `appsettings.json`.

---

## Minimal Configuration

All keys are in `appsettings.json`. Only the keys below are required to start the service.  
Everything else has a sensible default.

```json
{
  "PmsSecurity": {
    "FixedToken": "REQUIRED — set to a strong secret in production",
    "HeaderName": "X-PMS-TOKEN"
  },

  "RabbitMq": {
    "Host": "localhost",
    "Port": 5672,
    "VirtualHost": "/",
    "UserName": "guest",
    "Password": "guest"
  },

  "Queues": {
    "RetryDelaySeconds": 30,
    "MaxRetryAttempts": 3,
    "ProviderQueues": {
      "FAKE":  "q.pms.fake",
      "TIGER": "q.pms.tiger",
      "OPERA": "q.pms.opera"
    }
  },

  "Providers": {
    "TIGER": {
      "BaseUrl": "https://api.tiger-pms.example.com",
      "ApiKey": "",
      "ApiSecret": "",
      "TimeoutSeconds": 15
    },
    "OPERA": {
      "BaseUrl": "https://api.opera-pms.example.com",
      "ClientId": "",
      "ClientSecret": "",
      "TimeoutSeconds": 20
    },
    "FAKE": {
      "BaseUrl": "https://fake.provider.local",
      "ApiKey": "fake-api-key",
      "TimeoutSeconds": 10,
      "SimulateFailure": false,
      "SimulatedStatusCode": 503
    }
  }
}
```

> **Security:** Never commit real credentials. Use environment variables or a secrets manager in production.  
> `PmsSecurity:FixedToken` must be rotated per environment.

---

## Project Layout

```
PmsIntegration.sln
src/
  PmsIntegration.Host/          ← ASP.NET entry point + background consumers
  PmsIntegration.Application/   ← Use-cases, event routing, retry classification
  PmsIntegration.Core/          ← Contracts, interfaces, domain models (no deps)
  PmsIntegration.Infrastructure/← RabbitMQ, Serilog/Elastic, idempotency, clock
  PmsIntegration.Providers.Abstractions/  ← PmsProviderBase helper
  PmsIntegration.Providers.Fake/          ← Fake provider (testing/dev)
  PmsIntegration.Providers.Tiger/         ← Tiger PMS provider
  PmsIntegration.Providers.Opera/         ← Opera PMS provider
tests/
  PmsIntegration.Infrastructure.Tests/
  PmsIntegration.Providers.Fake.Tests/
```

---

## Further Reading

| Document | Purpose |
|---|---|
| [ARCHITECTURE.md](ARCHITECTURE.md) | Layer boundaries, dependency flow, job lifecycle, where to put new code |
| [PROVIDERS.md](PROVIDERS.md) | Add a new provider, template, DI rules, testing |
| [CONVENTIONS.md](CONVENTIONS.md) | Naming rules, logging fields, queue naming, forbidden references |
| [TROUBLESHOOTING.md](TROUBLESHOOTING.md) | Common failure modes and how to diagnose them |
