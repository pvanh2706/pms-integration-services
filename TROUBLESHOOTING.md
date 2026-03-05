# Troubleshooting

> Practical diagnosis guide for engineers operating the PMS Integration Service.  
> Start every investigation with the `correlationId` and `providerKey` from the failed event.

---

## Table of Contents

1. [General Diagnostic Approach](#general-diagnostic-approach)
2. [Provider Not Registered](#provider-not-registered)
3. [Duplicate ProviderCode](#duplicate-providercode)
4. [Authentication Failures (401)](#authentication-failures-401)
5. [Queue Stuck / Messages Not Processed](#queue-stuck--messages-not-processed)
6. [Messages Going Straight to DLQ](#messages-going-straight-to-dlq)
7. [Provider API Errors (4xx / 5xx)](#provider-api-errors-4xx--5xx)
8. [Idempotency False Positives (Duplicate Ignored)](#idempotency-false-positives-duplicate-ignored)
9. [Kibana Queries Reference](#kibana-queries-reference)

---

## General Diagnostic Approach

Every log entry in this service carries structured fields. The fastest way to diagnose any failure is:

1. **Get the `correlationId`** — from the `X-Correlation-Id` response header or from the PMS event log.
2. **Filter logs in Kibana** with `correlationId:<value>` to see the full pipeline trace.
3. **Add `providerKey`** to narrow down which provider failed.
4. **Check `auditAction`** to find the last known stage (`pms.received` → `job.enqueued` → `job.processing` → `job.success` / `job.retryable_failed` / `job.failed`). Jobs moved to DLQ are logged as `ILogger.LogWarning` (not an audit action); search for `message:*DLQ*`.

Useful Kibana query pattern:

```
correlationId:"<guid>" AND providerKey:"TIGER"
```

---

## Provider Not Registered

### Symptom

```
System.InvalidOperationException: No IPmsProvider registered for provider code 'ACME'. Registered codes: [FAKE, TIGER, OPERA]
```

The exception is thrown by `PmsProviderFactory.Get(providerCode)` inside `ProcessIntegrationJobHandler`.

### Causes and fixes

| Cause | How to verify | Fix |
|---|---|---|
| `AddAcmeProvider(configuration)` not called in `ProvidersServiceExtensions` | Search `ProvidersServiceExtensions.cs` for the provider name | Add the registration line |
| `ProjectReference` missing in `Host.csproj` | Run `dotnet build` — missing reference causes a compile error | Add `<ProjectReference>` to `Host.csproj` |
| ProviderCode mismatch between `IPmsProvider.ProviderKey` and the incoming message header `providerKey` | Log the incoming `providerKey` field and compare to `IPmsProvider.ProviderKey` | Align the strings (see [ProviderCode Rules](CONVENTIONS.md#providercode-rules)) |
| Provider registered after `PmsProviderFactory` in DI | The factory captures `IEnumerable<IPmsProvider>` at registration time | Move all `AddXxxProvider()` calls **before** `services.AddSingleton<IPmsProviderFactory, PmsProviderFactory>()` |

### How to list registered providers at runtime

`IPmsProviderFactory.RegisteredKeys` returns all registered keys. Log this at startup or expose it via a health-check endpoint.

```json
{
  "registeredProviders": ["FAKE", "TIGER", "OPERA"]
}
```

---

## Duplicate ProviderCode

### Symptom

Two providers are registered with the same `ProviderKey`, e.g. both return `"TIGER"`.  
`PmsProviderFactory` will throw at construction time:

```
System.InvalidOperationException: Duplicate provider key 'TIGER' detected. Provider type: PmsIntegration.Providers.Tiger.TigerProvider
```

### Causes and fixes

| Cause | How to verify | Fix |
|---|---|---|
| `AddTigerProvider()` called twice in `ProvidersServiceExtensions` | Read `ProvidersServiceExtensions.cs` | Remove the duplicate call |
| Two different classes both return the same `ProviderKey` | Grep for the duplicate key string in `Providers.*` projects | Rename one provider key and update all references |
| A provider project was renamed but the old DI registration was not removed | Check git diff for orphaned registrations | Remove stale registration |

---

## Authentication Failures (401)

### Symptom — PMS side

The PMS receives `HTTP 401 {"error":"unauthorized"}` when calling `POST /api/pms/events`.

### Causes and fixes

| Cause | How to verify | Fix |
|---|---|---|
| Missing `X-PMS-TOKEN` header | Check Kibana for `message:"unauthorized"` + the request path | Ensure the PMS sends the header on every request |
| Wrong token value | Compare the PMS token against `PmsSecurity:FixedToken` in `appsettings.json` | Update the token on the PMS side or rotate `PmsSecurity:FixedToken` |
| Wrong header name | Check `PmsSecurity:HeaderName` — should be `X-PMS-TOKEN` unless overridden | Align the header name in the PMS and the service config |
| Token has leading/trailing whitespace | Log the raw header value (`Request.Headers["X-PMS-TOKEN"]`) | Trim before comparing — or fix the PMS to send a clean value |

### Where to look in code

`PmsTokenMiddleware.InvokeAsync` in `src/PmsIntegration.Host/Middleware/PmsTokenMiddleware.cs`.  
The middleware logs nothing on failure by default; add a `Debug` log there during investigation if needed.

---

## Queue Stuck / Messages Not Processed

### Symptom

Messages accumulate in `q.pms.<provider>` but `job.processing` never appears in Kibana.

### Diagnosis steps

1. **Is RabbitMQ reachable?**

   ```bash
   # Management UI
   http://localhost:15672
   # or
   rabbitmqctl list_queues name messages consumers
   ```

   If `consumers = 0` for the provider queue, the consumer is not connected.

2. **Is the Host process running?**

   Check the service status or the process list.

3. **Did `ProviderConsumerOrchestrator` start all consumers?**

   Look for a startup log entry like:

   ```
   Starting consumer for provider TIGER on queue q.pms.tiger
   ```

   If missing, the provider may not be registered (see [Provider Not Registered](#provider-not-registered)).

4. **Is the queue name correct?**

   Compare `Queues:ProviderQueues:TIGER` in `appsettings.json` against the queue name visible in the RabbitMQ management UI.

5. **RabbitMQ connection dropped?**

   `RabbitMqConnectionFactory` uses `AutomaticRecoveryEnabled = true`, so transient disconnects are recovered at the connection level automatically.

   Additionally, `ProviderConsumerService` has an outer reconnect loop: if the channel closes unexpectedly after auto-recovery is exhausted, the service waits `RabbitMq:ConsumerReconnectDelaySeconds` (default 10 s) and re-establishes the connection and channel on its own. **A process restart is not required for reconnection.**

   Look for these log entries to monitor reconnection:
   ```
   Consumer for queue q.pms.tiger lost connection. Reconnecting in 10s.
   Consumer connecting to queue: q.pms.tiger
   Consumer active on queue: q.pms.tiger
   ```

### Quick reset (non-production)

```powershell
# Restart the service to reconnect consumers
Restart-Service PmsIntegrationService
```

---

## Messages Going Straight to DLQ

### Symptom

After `MaxRetryAttempts` retries, messages appear in `q.pms.<provider>.dlq`.
Look for `LogWarning` entries containing "DLQ" in the logs (the DLQ routing is logged via `ILogger` in `ProviderConsumerService`, not via `IAuditLogger`).

### Diagnosis steps

1. **Find the last error** in Kibana:

   ```
   correlationId:"<guid>" AND (auditAction:"job.failed" OR auditAction:"job.retryable_failed")
   ```

   Examine `x-last-error-code` and `x-last-error-message` fields.

2. **Classify the failure:**

   | Error type | Likely cause |
   |---|---|
   | `5xx` from provider API | Provider is down or overloaded — retries are expected |
   | `4xx` (e.g. 400, 422) | Mapping error — the `IntegrationJob.Data` produced an invalid request body |
   | `401` from provider API | Provider API credentials are wrong or expired |
   | `TaskCanceledException` | Request timeout — increase `Providers:<KEY>:TimeoutSeconds` |
   | Serialization exception | `IntegrationJob.Data` contains unexpected format |

3. **For mapping errors:**

   - Inspect `ProviderRequest.Body` logged at `DEBUG` level.
   - Run the mapper in isolation with a unit test using the failing payload.
   - Fix the mapper and redeploy.

4. **Replaying DLQ messages** (manual):

   Messages in `.dlq` are held indefinitely. To replay:
   - Fix the root cause first.
   - Use the RabbitMQ management UI to shovel / move messages from `.dlq` back to the main queue.
   - Or publish a new event from the PMS with the same payload.

   > **TODO:** Document a replay script if one is added to the project.

---

## Provider API Errors (4xx / 5xx)

### Retry behavior by status code

| Status code | Outcome | Action |
|---|---|---|
| 200–299 | `Success` | ACK |
| 408 Request Timeout | `RetryableFailure` | Retry |
| 429 Too Many Requests | `RetryableFailure` | Retry |
| 5xx | `RetryableFailure` | Retry |
| 400 Bad Request | `NonRetryableFailure` | DLQ immediately |
| 401 Unauthorized | `NonRetryableFailure` | DLQ immediately |
| 403 Forbidden | `NonRetryableFailure` | DLQ immediately |
| 404 Not Found | `NonRetryableFailure` | DLQ immediately |
| 422 Unprocessable Entity | `NonRetryableFailure` | DLQ immediately |

Classification logic lives in `Application/Services/RetryClassifier.cs`.

### 401 from provider API

The provider's own API is rejecting the credentials stored in `Providers:<KEY>:ApiKey` / `ApiSecret` / `ClientId` / `ClientSecret` in `appsettings.json`.

Steps:
1. Rotate the credentials in the provider portal.
2. Update `appsettings.json` (or the secrets manager).
3. Restart the service.
4. Replay affected DLQ messages.

### 429 rate limit

Increase `Queues:RetryDelaySeconds` to slow down retries, or contact the provider about rate limit quotas.

---

## Idempotency False Positives (Duplicate Ignored)

### Symptom

A valid new event is logged with `auditAction:"job.duplicate_ignored"` even though it is not a retry.

### Idempotency key

```
{hotelId}:{eventId}:{eventType}:{providerKey}
```

If the PMS sends an event with the same `hotelId`, `eventId`, and `eventType` within the TTL window (default 24 hours), the service treats it as a duplicate.

### Causes and fixes

| Cause | Fix |
|---|---|
| PMS re-sends an event with the same `eventId` for a legitimate new event | PMS must use a distinct `eventId` per event |
| `eventId` generation bug on PMS side | Fix the PMS to generate unique `eventId` values |
| `InMemoryIdempotencyStore` was cleared by a restart, but the PMS is resending old retries | Expected behavior — if the original succeeded, the duplicate is correctly ignored |
| TTL set too long | Reduce `IIdempotencyStore` acquire TTL (code change required) |

### How to check

In Kibana:

```
eventId:"EVT-001" AND hotelId:"H001" AND auditAction:"job.duplicate_ignored"
```

Also search for the original `job.success` to confirm the event was processed correctly the first time.

---

## Kibana Queries Reference

Use these in the Kibana 6.8.23 Discover view with the single application index.

| Goal | Query |
|---|---|
| Full trace for one event | `correlationId:"<guid>"` |
| All retryable failures for a provider | `providerKey:"TIGER" AND auditAction:"job.retryable_failed"` |
| All non-retryable failures for a provider | `providerKey:"TIGER" AND auditAction:"job.failed"` |
| Jobs moved to DLQ | `level:"Warning" AND message:*DLQ*` (DLQ routing is logged via `ILogger.LogWarning`, not via `IAuditLogger`) |
| Auth failures into the service | `message:"unauthorized"` |
| Duplicate-ignored events | `auditAction:"job.duplicate_ignored"` |
| All events for a hotel | `hotelId:"H001"` |
| Retries for a specific job | `jobId:"<guid>"` |
| Slow provider calls (timeout) | `providerKey:"OPERA" AND auditAction:"job.retryable_failed" AND x-last-error-code:"TIMEOUT"` |
| Provider not registered | `auditAction:"job.provider_not_registered"` |

**Tip:** Pin `correlationId` as a column in Kibana Discover to quickly scan related log lines across a single request.
