# ADR-003: One RabbitMQ Queue per Provider

| Field | Value |
|---|---|
| Status | Accepted |
| Date | 2026-03-04 |
| Deciders | TODO: record names |
| Supersedes | — |

---

## Context

The service must process `IntegrationJob` messages asynchronously for multiple providers
(Tiger, Opera, Fake, …).

A design decision was required on how to partition the message queues:

| Option | Description |
|---|---|
| **Single shared queue** | All providers share one queue; consumer reads the message and dispatches by `providerKey`. |
| **One queue per provider (selected)** | Each provider has its own main queue, retry queue, and dead-letter queue. |
| **One queue per event type** | Partitioned by `eventType` (Checkin, Checkout, …) regardless of provider. |

Constraints:
- RabbitMQ is the message broker.
- The retry mechanism must not cause a hot-loop (no `BasicNack(requeue:true)`).
- Retry delay and max attempts must be configurable.
- Failed messages must be preserved for manual inspection in a DLQ.
- The consumer must not need to know every provider at compile time.

---

## Decision

**Each provider is assigned three dedicated RabbitMQ queues:**

| Queue | Name pattern | Purpose |
|---|---|---|
| Main | `q.pms.<providerKeyLower>` | Normal message delivery |
| Retry | `q.pms.<providerKeyLower>.retry` | TTL-delayed redelivery back to main |
| DLQ | `q.pms.<providerKeyLower>.dlq` | Unrecoverable or exhausted messages |

### Queue naming

Queue names are configured in `appsettings.json`. The service never hard-codes them:

```json
"Queues": {
  "RetryDelaySeconds": 30,
  "MaxRetryAttempts": 3,
  "ProviderQueues": {
    "FAKE":  "q.pms.fake",
    "TIGER": "q.pms.tiger",
    "OPERA": "q.pms.opera"
  }
}
```

`RabbitMqTopology` derives retry and DLQ names by appending `.retry` / `.dlq` to the configured main queue name.

### Topology declaration

`RabbitMqTopology.DeclareProviderQueuesAsync(mainQueue)` creates the three queues at startup
for each registered provider. Retry queue uses `x-message-ttl` and `x-dead-letter-routing-key`
to dead-letter back to the main queue after the configured delay:

```csharp
// RabbitMqTopology.cs (simplified)
var retryArgs = new Dictionary<string, object?>
{
    ["x-message-ttl"]             = (int)TimeSpan.FromSeconds(_queueOptions.RetryDelaySeconds).TotalMilliseconds,
    ["x-dead-letter-exchange"]    = "",
    ["x-dead-letter-routing-key"] = mainQueue
};
await channel.QueueDeclareAsync(retryQueue, durable: true, ...arguments: retryArgs);
await channel.QueueDeclareAsync(mainQueue,  durable: true, ...arguments: null);
await channel.QueueDeclareAsync(dlqQueue,   durable: true, ...arguments: null);
```

### Consumer orchestration

`ProviderConsumerOrchestrator` (an `IHostedService`) discovers all registered provider keys
from `IPmsProviderFactory.RegisteredKeys` and starts one `ProviderConsumerService` per provider.
No changes to `Background/` are needed when a new provider is added.

### ACK / retry / DLQ flow

Consumer decisions based on `IntegrationResult.Outcome`:

```
Success              → ACK
RetryableFailure     → publish to <main>.retry, increment x-retry-attempt, ACK original
                       (after RetryDelaySeconds the message returns to main queue via DLX)
NonRetryableFailure  → publish to <main>.dlq, ACK original
attempt ≥ MaxRetryAttempts → publish to <main>.dlq, ACK original
```

The `x-retry-attempt` counter is stored in the AMQP message headers and incremented on each
retry. Additional diagnostic headers added on failure:

- `x-last-error-code`
- `x-last-error-message`

**`BasicNack(requeue: true)` is explicitly forbidden** — it re-queues the message immediately,
creating a hot retry loop with no back-off.

---

## Alternatives Considered

### A — Single shared queue

All providers share `q.pms.events`. The consumer reads `providerKey` from the message and
dispatches internally.

**Rejected because:**
- A slow or failing provider (e.g. Tiger is down) blocks processing of all other providers'
  messages that are queued behind it — head-of-line blocking.
- Cannot tune retry delay or max attempts per provider.
- Consumer throughput of one provider is throttled by the slowest provider.
- A DLQ would accumulate messages from all providers, making diagnosis harder.

### B — One queue per event type

Separate queues for `Checkin`, `Checkout`, etc., regardless of provider.

**Rejected because:**
- The bottleneck is the provider API, not the event type.
- A single event type (e.g. Checkin) sent to Tiger and Opera would need to be split into
  per-provider messages anyway — the same problem as a shared queue, restated.

### C — One queue per provider per event type

Granular: `q.pms.tiger.checkin`, `q.pms.tiger.checkout`, etc.

**Rejected because:**
- Exponential queue proliferation as providers and event types grow.
- No meaningful benefit over one-queue-per-provider given that retry behavior is per-provider,
  not per-event-type.

---

## Consequences

### Positive

- **Isolation:** Tiger being down does not delay Opera processing.
- **Independent tuning:** `RetryDelaySeconds` and `MaxRetryAttempts` apply globally today;
  the config structure (`Queues:ProviderQueues`) makes per-provider overrides straightforward
  in future without a topology redesign.
- **No hot-loop:** The TTL-based retry queue guarantees a minimum back-off equal to
  `RetryDelaySeconds` before redelivery.
- **Zero-code consumer growth:** `ProviderConsumerOrchestrator` discovers queues from
  `IPmsProviderFactory.RegisteredKeys` — adding a provider automatically starts a new consumer.
- **DLQ isolation:** Unrecoverable Tiger messages are in `q.pms.tiger.dlq`, not mixed with Opera.

### Negative / Trade-offs

- Each new provider creates three RabbitMQ queues. For a large number of providers this could
  become a management concern, but is not a technical scalability problem for RabbitMQ.
- `RetryDelaySeconds` and `MaxRetryAttempts` are currently global. Per-provider delay
  configuration requires a schema change to `QueueOptions`.
- DLQ replay is manual (RabbitMQ management UI shovel or a custom script).
  TODO: a replay utility is not yet implemented.

---

## How to Implement

When adding a new provider:

1. Add the queue name to `appsettings.json`:
   ```json
   "Queues": {
     "ProviderQueues": {
       "ACME": "q.pms.acme"
     }
   }
   ```
2. Register the provider in `ProvidersServiceExtensions` (see ADR-001).
   `RabbitMqTopology` and `ProviderConsumerOrchestrator` pick up the new queue automatically
   from `IPmsProviderFactory.RegisteredKeys`.
3. No changes to `RabbitMqTopology`, `ProviderConsumerOrchestrator`, or `ProviderConsumerService`.

---

## How to Validate

1. **Queue declaration:** after startup, the RabbitMQ management UI (http://localhost:15672)
   must show all three queues per provider (`q.pms.acme`, `q.pms.acme.retry`, `q.pms.acme.dlq`).
2. **Retry flow:** set `FakeOptions:SimulateFailure = true`, publish a job to `q.pms.fake`;
   verify messages appear in `q.pms.fake.retry` and then return to `q.pms.fake` after
   `RetryDelaySeconds`, repeating until `MaxRetryAttempts` is reached, then landing in
   `q.pms.fake.dlq`.
3. **Isolation:** stall Tiger's consumer (`SimulateFailure`) and verify Opera events continue
   to process without delay.
4. **No requeue:** search the codebase for `BasicNack` with `requeue: true`; result must be empty.
5. **x-retry-attempt header:** inspect a message in `q.pms.fake.retry` via management UI;
   the `x-retry-attempt` header must be present and increment on each retry.
6. **Startup log:** `ProviderConsumerOrchestrator` must log one `"Starting consumer for provider X"` line per registered provider.
