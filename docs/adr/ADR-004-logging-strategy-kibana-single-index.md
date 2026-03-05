# ADR-004: Logging Strategy — Serilog, Single Elasticsearch Index, Kibana 6.8.23

| Field | Value |
|---|---|
| Status | Accepted |
| Date | 2026-03-04 |
| Deciders | TODO: record names |
| Supersedes | — |
| Constraint | Kibana version is fixed at **6.8.23** and cannot be upgraded. |

---

## Context

The service must produce structured, queryable logs for:

1. **Operational diagnostics** — trace a PMS event through the full pipeline using a
   `correlationId`.
2. **Audit trail** — record discrete audit events (`pms.received`, `job.success`,
   `job.dlq`, etc.) for each `IntegrationJob`.
3. **Failure analysis** — surface the provider key, HTTP status, and error message when
   a job fails or is retried.

Constraints:
- The Elasticsearch / Kibana stack is fixed at **Kibana 6.8.23**.
- Kibana 6.x does not support ILM index rollover in the UI. A single index pattern
  simplifies management.
- The team operates one deployment environment per stage (dev, staging, production).
  Per-provider or per-event-type indices add operational overhead with no proportional benefit.
- Log shipping must not be a bottleneck on the event processing path.

---

## Decision

### 1. Serilog as the logging framework

`Serilog` is the structured logging library throughout the solution.

- Configuration is centralised in `SerilogElasticSetup.Configure` (in
  `PmsIntegration.Infrastructure/Logging/`).
- The Host bootstraps Serilog before the DI container is built:

  ```csharp
  // Program.cs
  Log.Logger = SerilogElasticSetup
      .Configure(new LoggerConfiguration(), builder.Configuration)
      .CreateLogger();
  builder.Host.UseSerilog();
  ```

- All other layers use `Microsoft.Extensions.Logging.ILogger<T>` injected by DI;
  Serilog fulfils that abstraction at runtime via `UseSerilog()`.
- `PmsIntegration.Core` and `PmsIntegration.Application` **must not** reference Serilog
  directly — they log through `ILogger<T>` only.

### 2. A single Elasticsearch index for all log entries

All application log entries (all layers, all providers) are written to a single index.
The index name is configured in `appsettings.json`.

> **TODO:** Document the exact config key once the `SerilogElasticSetup` index format
> argument is confirmed (expected: `Serilog:WriteTo:0:Args:indexFormat` or similar).

**Why a single index:**
- Kibana 6.x index pattern management is manual. Multiple indices require multiple index
  patterns and separate Discover tabs.
- Correlation across providers (e.g. tracing one PMS event that fans out to Tiger and Opera)
  requires both logs in the same query context.
- Provider filtering is achieved via the `providerKey` structured field — no index split needed.

### 3. Mandatory structured log fields

All log entries — regardless of log level — must include these fields as Serilog properties
(using `LogContext.PushProperty` or message template destructuring):

| Field | Type | Description |
|---|---|---|
| `correlationId` | `string` (GUID) | Traces one PMS event end-to-end |
| `hotelId` | `string` | Property/hotel identifier from the event |
| `eventId` | `string` | Unique event identifier from the PMS |
| `eventType` | `string` | e.g. `Checkin`, `Checkout` |
| `providerKey` | `string` | Uppercase provider code, e.g. `TIGER` |
| `jobId` | `string` (GUID) | `IntegrationJob` identifier (set when available) |
| `attempt` | `int` | Retry attempt number (set when available) |

### 4. Audit events via ElasticAuditLogger

Domain-significant moments in the pipeline are recorded as **audit events** by
`ElasticAuditLogger` (`IAuditLogger` implementation in `Infrastructure/Logging/`).

Fixed audit action strings:

| `auditAction` | When |
|---|---|
| `pms.received` | Event accepted by `PmsEventController` |
| `job.enqueued` | `IntegrationJob` published to a provider queue |
| `job.processing` | Consumer picks up a job and begins processing |
| `job.success` | Provider API call returned a successful response |
| `job.failed` | Provider API call failed (retryable or non-retryable) |
| `job.dlq` | Job moved to the dead-letter queue |
| `job.duplicate_ignored` | Idempotency check rejected the job as a duplicate |

Log format:

```
AUDIT {auditAction} {@Data}
```

Example rendered JSON in Elasticsearch:

```json
{
  "@timestamp": "2026-03-04T10:22:01.123Z",
  "level": "Information",
  "message": "AUDIT job.success",
  "auditAction": "job.success",
  "correlationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "hotelId": "H001",
  "eventId": "EVT-001",
  "eventType": "Checkin",
  "providerKey": "TIGER",
  "jobId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "attempt": 1
}
```

### 5. CorrelationId propagation

The `correlationId` must appear in every log line related to a job:

| Stage | Mechanism |
|---|---|
| HTTP inbound | `ReceivePmsEventHandler` generates or inherits `CorrelationId` from `PmsEventEnvelope` |
| HTTP response | `PmsEventController` sets `X-Correlation-Id` response header |
| `IntegrationJob` | `IntegrationJob.CorrelationId` carries it |
| RabbitMQ message | `correlationId` AMQP header |
| Provider HTTP request | `CorrelationIdHandler` (delegating handler) adds `X-Correlation-Id` header |
| Consumer log scope | `LogContext.PushProperty("correlationId", ...)` inside the consumer |

---

## Alternatives Considered

### A — Per-provider index (`pms-tiger-*`, `pms-opera-*`)

Each provider writes to its own index.

**Rejected because:**
- Cross-provider correlation queries require Kibana multi-index patterns, which are awkward
  in Kibana 6.x.
- A PMS event fans out to multiple providers; tracing it end-to-end requires a single query.
- Adds index management overhead without benefit at the current scale.

### B — Per-component index (`pms-host-*`, `pms-application-*`)

Each architectural layer writes to a separate index.

**Rejected because:**
- Traces a single request across multiple indices — the opposite of what engineers need during
  incident diagnosis.
- Kibana 6.x does not support cross-index correlation natively.

### C — Application Insights / OpenTelemetry

Replace Serilog + Elasticsearch with a distributed tracing platform.

**Rejected because:**
- The Kibana 6.8.23 stack is an existing operational constraint that cannot be changed.
- OpenTelemetry / Application Insights require infrastructure changes outside the team's
  current scope.
- Could be revisited in a future ADR if the Kibana constraint is lifted.

### D — Separate audit log to a relational database

Write `IAuditLogger` events to SQL in addition to (or instead of) Elasticsearch.

**Rejected because:**
- Adds a synchronous write dependency on the hot processing path.
- Elasticsearch already provides queryable structured storage that satisfies the audit
  requirement.
- TODO: evaluate if a SQL audit trail is required for compliance reasons.

---

## Consequences

### Positive

- Single Kibana index pattern covers the full pipeline — one Discover tab for all diagnosis.
- `correlationId` as a consistent field allows a one-click pivot from any log line to all
  related lines.
- `auditAction` as a fixed vocabulary prevents free-text audit log fragmentation.
- Serilog's `LogContext` enrichment keeps field population out of business code; handlers
  push properties to the ambient context.
- Decoupled: `Application` and `Core` use `ILogger<T>` — swapping Serilog for another
  provider requires changes only in `Infrastructure` and `Host`.

### Negative / Trade-offs

- A single index grows unboundedly without an ILM policy (Kibana 6.x does not have ILM UI).
  Index retention must be managed via Elasticsearch curator or a cron job.
  TODO: document the retention/rollover strategy.
- All log levels (Debug, Info, Warning, Error) land in the same index. High-volume Debug
  logging in production will inflate index size. Set `Default` log level to `Information`
  in production (`appsettings.json` Logging:LogLevel:Default).
- If `SerilogElasticSetup` loses connectivity to Elasticsearch, logs are dropped unless
  a fallback sink (e.g. file) is configured.
  TODO: add a file sink as a fallback for production.

---

## How to Implement

1. `SerilogElasticSetup.Configure` (in `Infrastructure/Logging/`) must:
   - Read Elasticsearch URL and index format from `appsettings.json`.
   - Configure `Enrich.FromLogContext()` so `LogContext.PushProperty` fields appear on every event.
   - TODO: document and verify the exact config key path.

2. In every consumer and handler, push the mandatory fields to `LogContext`:

   ```csharp
   using (LogContext.PushProperty("correlationId", job.CorrelationId))
   using (LogContext.PushProperty("providerKey",   job.ProviderKey))
   using (LogContext.PushProperty("jobId",          job.JobId))
   using (LogContext.PushProperty("attempt",        attempt))
   {
       // processing code here
   }
   ```

3. `ElasticAuditLogger.Log(auditAction, data)` must use `Serilog.Log.ForContext(...)` with the
   fixed `auditAction` property to enable Kibana filtering.

4. Never call `Serilog.Log.*` or `ILogger<T>` directly from `Core`. Use `IAuditLogger`
   (injected via DI) for audit events in `Application`.

---

## How to Validate

1. **Kibana index pattern:** after starting the service and sending one event, open Kibana →
   Management → Index Patterns; verify the configured index exists with `@timestamp` as the
   time field.

2. **Mandatory fields:** run this query in Kibana Discover; all results must have all
   mandatory fields populated:

   ```
   auditAction:"job.success"
   ```

   Check that `correlationId`, `hotelId`, `eventId`, `eventType`, `providerKey`, `jobId`,
   and `attempt` are all non-null.

3. **Full trace query:** take the `X-Correlation-Id` from a test event response and run:

   ```
   correlationId:"<guid>"
   ```

   Results should include: `pms.received`, `job.enqueued`, `job.processing`, `job.success`
   (in that order by `@timestamp`).

4. **Audit vocabulary:** `grep -rn "AUDIT " src/` — all log sites must use only the
   fixed `auditAction` strings listed in this ADR.

5. **No Serilog in Core/Application:** `grep -rn "using Serilog" src/PmsIntegration.Core src/PmsIntegration.Application` must return no results.

6. **Provider isolation in Kibana:** filter by `providerKey:"TIGER"` and confirm only
   Tiger-related log lines appear.
