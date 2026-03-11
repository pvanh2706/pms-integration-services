# Tầng Application — Mô tả chi tiết

> Dành cho team dev — giải thích vai trò, thành phần và luồng xử lý của `PmsIntegration.Application`

---

## 1. Application là gì?

`PmsIntegration.Application` là tầng **điều phối use case**. Tầng này trả lời câu hỏi:

> **"Hệ thống cần làm gì?"** — không phải "làm như thế nào về mặt kỹ thuật"

Application không biết:
- ❌ RabbitMQ publish hoạt động như thế nào chi tiết
- ❌ HTTP request được gửi bằng `HttpClient` hay gì khác
- ❌ Log được ghi lên Elasticsearch hay Console
- ❌ ASP.NET pipeline trông như thế nào

Application chỉ biết:
- ✅ Nhận event từ PMS → validate → fan-out → đẩy vào queue
- ✅ Nhận job từ queue → check idempotency → gọi provider → phân loại kết quả
- ✅ Gọi interface từ Core, không quan tâm ai implement

---

## 2. Phụ thuộc

```
PmsIntegration.Application
    │
    └── PmsIntegration.Core          ← ProjectReference duy nhất
            (IPmsProviderFactory, IIdempotencyStore, IQueuePublisher,
             IAuditLogger, IClock, IConfigProvider, IProviderFlowTracker,
             IntegrationJob, PmsEventEnvelope, IntegrationResult, ...)
```

**Packages bên ngoài (tối giản):**
- `Microsoft.Extensions.DependencyInjection.Abstractions` — chỉ để đăng ký DI
- `Microsoft.Extensions.Logging.Abstractions` — chỉ để inject `ILogger<T>`
- `Microsoft.Extensions.Options` — chỉ để đọc `IOptions<T>`

Không có NuGet nào của RabbitMQ, Elasticsearch, hay HTTP client.

---

## 3. Cấu trúc thư mục

```
PmsIntegration.Application/
├── UseCases/
│   ├── ReceivePmsEventHandler.cs       ← Use case 1: nhận event từ PMS
│   └── ProcessIntegrationJobHandler.cs ← Use case 2: xử lý job từ queue
├── Services/
│   ├── EventValidator.cs               ← Validate envelope đầu vào
│   ├── ProviderRouter.cs               ← Xác định tên queue cho provider
│   └── RetryClassifier.cs              ← Phân loại lỗi: retry hay DLQ
└── DI/
    └── ApplicationServiceExtensions.cs ← Đăng ký DI cho tầng Application
```

---

## 4. Use Cases

### 4.1 `ReceivePmsEventHandler` — Nhận event từ PMS

**Được gọi bởi:** `PmsEventController` (Host) khi nhận HTTP POST `/api/pms/events`

**Luồng xử lý:**

```
PmsEventEnvelope
    │
    ▼
[1] Audit log "pms.received"
    │
    ▼
[2] EventValidator.Validate(envelope)
    │  → Throw ArgumentException nếu thiếu HotelId / EventId / EventType / Providers
    │
    ▼
[3] Fan-out: tạo 1 IntegrationJob cho mỗi provider
    │  VD: Providers = ["TIGER", "OPERA"]
    │      → Job { ProviderKey: "TIGER", JobId: uuid-1 }
    │      → Job { ProviderKey: "OPERA", JobId: uuid-2 }
    │
    ▼
[4] Với mỗi job:
    │  ├── ProviderRouter.ResolveQueue(providerKey) → tên queue
    │  ├── IQueuePublisher.PublishAsync(job, queue)
    │  └── Audit log "job.enqueued"
    │
    ▼
[5] Trả về correlationId
```

**Code tóm tắt:**
```csharp
public async Task<string> HandleAsync(PmsEventEnvelope envelope, CancellationToken ct)
{
    var correlationId = envelope.CorrelationId ?? Guid.NewGuid().ToString();

    _audit.Log("pms.received", new { correlationId, envelope.HotelId, ... });

    _validator.Validate(envelope);  // Throw nếu invalid

    var jobs = envelope.Providers.Select(p => new IntegrationJob
    {
        ProviderKey   = p.Trim().ToUpperInvariant(),
        CorrelationId = correlationId,
        CreatedAtUtc  = _clock.UtcNow,
        // ... copy các field từ envelope
    }).ToList();

    foreach (var job in jobs)
    {
        var queue = _router.ResolveQueue(job.ProviderKey);
        await _publisher.PublishAsync(job, queue, ct);
        _audit.Log("job.enqueued", new { job.JobId, job.ProviderKey, queue });
    }

    return correlationId;
}
```

**Dependencies được inject:**

| Dependency | Interface từ | Tác dụng |
|---|---|---|
| `EventValidator` | Application (concrete) | Validate envelope |
| `ProviderRouter` | Application (concrete) | Resolve tên queue |
| `IQueuePublisher` | Core | Publish job vào RabbitMQ |
| `IConfigProvider` | Core | Đọc config queue name |
| `IAuditLogger` | Core | Ghi audit log |
| `IClock` | Core | Lấy thời gian `CreatedAtUtc` |

---

### 4.2 `ProcessIntegrationJobHandler` — Xử lý job từ queue

**Được gọi bởi:** `ProviderConsumerService` (Host) khi nhận message từ RabbitMQ

**Luồng xử lý:**

```
IntegrationJob (từ queue)
    │
    ▼
[1] Audit log "job.processing"
    │
    ▼
[2] IIdempotencyStore.TryAcquireAsync(key)
    │  key = "{hotelId}:{eventId}:{eventType}:{providerKey}"
    │  → false (đã xử lý trước đó) → Audit "job.duplicate_ignored" → return Succeeded()
    │  → true  → tiếp tục
    │
    ▼
[3] IPmsProviderFactory.Get(job.ProviderKey)
    │  → InvalidOperationException (provider chưa đăng ký)
    │      → Audit "job.provider_not_registered"
    │      → return NonRetryableFailed("PROVIDER_NOT_REGISTERED")
    │
    ▼
[4] provider.BuildRequestAsync(job)  → ProviderRequest
    │  flowTracker?.OnRequestBuilt(request)  ← ghi log request body (nếu có tracker)
    │
    ▼
[5] provider.SendAsync(request)  → ProviderResponse
    │  flowTracker?.OnResponseReceived(response)  ← ghi log response (nếu có tracker)
    │
    ▼
[6] Phân tích response:
    │  ├── IsSuccess (2xx)
    │  │       → IIdempotencyStore.MarkSuccessAsync (gia hạn TTL 7 ngày)
    │  │       → Audit "job.success"
    │  │       → return Succeeded()
    │  │
    │  └── Non-2xx
    │           → RetryClassifier.ClassifyHttpStatus(statusCode)
    │           → Audit "job.retryable_failed" hoặc "job.failed"
    │           → return RetryableFailed(...) hoặc NonRetryableFailed(...)
    │
    ▼ (nếu exception)
[7] RetryClassifier.ClassifyException(ex)
    │  → Audit "job.failed"
    │  → return RetryableFailed hoặc NonRetryableFailed
```

**Code tóm tắt:**
```csharp
public async Task<IntegrationResult> HandleAsync(
    IntegrationJob job,
    IProviderFlowTracker? flowTracker = null,
    CancellationToken ct = default)
{
    var key = $"{job.HotelId}:{job.EventId}:{job.EventType}:{job.ProviderKey}";

    if (!await _idempotency.TryAcquireAsync(key, AcquireTtl, ct))
        return IntegrationResult.Succeeded(); // duplicate — bỏ qua

    var provider = _providerFactory.Get(job.ProviderKey); // throw → NonRetryable

    var request  = await provider.BuildRequestAsync(job, ct);
    flowTracker?.OnRequestBuilt(request);

    var response = await provider.SendAsync(request, ct);
    flowTracker?.OnResponseReceived(response);

    if (response.IsSuccess)
    {
        await _idempotency.MarkSuccessAsync(key, SuccessTtl, ct);
        return IntegrationResult.Succeeded();
    }

    return _classifier.ClassifyHttpStatus(response.StatusCode);
}
```

**TTL của idempotency:**

| Giai đoạn | TTL | Lý do |
|---|---|---|
| `TryAcquireAsync` | 24 giờ | Giữ lock trong thời gian đủ để retry hoàn thành |
| `MarkSuccessAsync` | 7 ngày | Chặn duplicate lâu dài sau khi thành công |

**`flowTracker` là gì?**

Tham số `IProviderFlowTracker? flowTracker` là optional callback để ghi log flow vào Elasticsearch — nhưng `ProcessIntegrationJobHandler` **không biết** đó là Elasticsearch. Đây là ứng dụng của Dependency Inversion: Host truyền adapter vào, handler chỉ gọi interface.

> Xem chi tiết tại [CLEAN_ARCHITECTURE.vi.md](CLEAN_ARCHITECTURE.vi.md) — Mục 4.

---

## 5. Services

### 5.1 `EventValidator` — Validate đầu vào

**Loại:** Concrete class (không phải interface) — logic đủ đơn giản, không cần swap implementation.

**Các rule:**

| Field | Điều kiện | Lỗi |
|---|---|---|
| `HotelId` | Không được rỗng/null | `ArgumentException` |
| `EventId` | Không được rỗng/null | `ArgumentException` |
| `EventType` | Không được rỗng/null | `ArgumentException` |
| `Providers` | Phải có ít nhất 1 provider | `ArgumentException` |

**Hành vi khi lỗi:** Throw `ArgumentException` → Controller bắt → trả về HTTP 400.

---

### 5.2 `ProviderRouter` — Xác định tên queue

**Mục đích:** Từ `providerKey` (ví dụ `"TIGER"`) → tên queue RabbitMQ (ví dụ `"q.pms.tiger"`).

**Logic:**

```csharp
public string ResolveQueue(string providerKey)
{
    var normalized = providerKey.Trim().ToUpperInvariant();

    // Ưu tiên 1: cấu hình tường minh trong appsettings
    var configuredQueue = _config.Get($"Queues:ProviderQueues:{normalized}");
    if (!string.IsNullOrWhiteSpace(configuredQueue))
        return configuredQueue;

    // Ưu tiên 2: convention mặc định
    return $"q.pms.{providerKey.Trim().ToLowerInvariant()}";
}
```

**Convention tên queue:**

| Provider | Queue mặc định | Queue retry | Queue DLQ |
|---|---|---|---|
| `TIGER` | `q.pms.tiger` | `q.pms.tiger.retry` | `q.pms.tiger.dlq` |
| `OPERA` | `q.pms.opera` | `q.pms.opera.retry` | `q.pms.opera.dlq` |
| `FAKE` | `q.pms.fake` | `q.pms.fake.retry` | `q.pms.fake.dlq` |

Để override: thêm vào `appsettings.json`:
```json
"Queues": {
  "ProviderQueues": {
    "TIGER": "my-custom-tiger-queue"
  }
}
```

---

### 5.3 `RetryClassifier` — Phân loại lỗi

**Mục đích:** Từ HTTP status code hoặc exception → quyết định retry hay gửi DLQ.

**Phân loại HTTP status:**

| Status code | Kết quả | Lý do |
|---|---|---|
| `2xx` | `Succeeded` | Thành công |
| `408` (Request Timeout) | `RetryableFailure` | Timeout tạm thời |
| `429` (Too Many Requests) | `RetryableFailure` | Rate limit — chờ rồi thử lại |
| `5xx` | `RetryableFailure` | Lỗi server tạm thời |
| `4xx` còn lại | `NonRetryableFailure` | Lỗi client — retry sẽ không giúp ích |

**Phân loại Exception:**

| Exception | Kết quả | Lý do |
|---|---|---|
| `TaskCanceledException`, `TimeoutException` | `RetryableFailure` | Timeout mạng |
| `HttpRequestException` | `RetryableFailure` | Lỗi kết nối tạm thời |
| `ArgumentException`, `InvalidOperationException` | `NonRetryableFailure` | Lỗi mapping/config |
| Tất cả còn lại | `RetryableFailure` | An toàn hơn là retry |

**Consumer dùng kết quả này để:**
- `RetryableFailure` → publish vào `.retry` queue
- `NonRetryableFailure` → publish vào `.dlq` queue

---

## 6. DI Registration

```csharp
// ApplicationServiceExtensions.cs
public static IServiceCollection AddApplication(this IServiceCollection services)
{
    services.AddTransient<ReceivePmsEventHandler>();      // Tạo mới mỗi HTTP request
    services.AddTransient<ProcessIntegrationJobHandler>(); // Tạo mới mỗi message
    services.AddSingleton<ProviderRouter>();               // Stateless — chia sẻ toàn app
    services.AddSingleton<EventValidator>();               // Stateless — chia sẻ toàn app
    services.AddSingleton<RetryClassifier>();              // Stateless — chia sẻ toàn app
    return services;
}
```

**Được gọi trong `Program.cs`:**
```csharp
builder.Services.AddApplication();
```

---

## 7. Luồng tổng thể qua Application

```
══════════════════════════════════════════════
  LUỒNG 1: Nhận event từ PMS (Inbound)
══════════════════════════════════════════════

HTTP POST /api/pms/events
        │  (Host: PmsEventController)
        ▼
ReceivePmsEventHandler.HandleAsync(envelope)
        │
        ├─[validate]──── EventValidator.Validate()
        │                  Throw 400 nếu thiếu field
        │
        ├─[fan-out]───── Tạo N IntegrationJob (1 per provider)
        │
        ├─[route]──────── ProviderRouter.ResolveQueue(providerKey)
        │                  → "q.pms.tiger", "q.pms.opera", ...
        │
        └─[publish]────── IQueuePublisher.PublishAsync(job, queue)
                           → RabbitMQ (impl ở Infrastructure)

══════════════════════════════════════════════
  LUỒNG 2: Xử lý job từ queue (Outbound)
══════════════════════════════════════════════

RabbitMQ message received
        │  (Host: ProviderConsumerService)
        ▼
ProcessIntegrationJobHandler.HandleAsync(job, flowTracker)
        │
        ├─[idempotency]── IIdempotencyStore.TryAcquireAsync()
        │                  → false: bỏ qua (duplicate)
        │                  → true: tiếp tục
        │
        ├─[resolve]──────  IPmsProviderFactory.Get(providerKey)
        │                  → TigerProvider / OperaProvider / ...
        │
        ├─[build]─────────  provider.BuildRequestAsync(job)
        │                   flowTracker?.OnRequestBuilt(request)
        │
        ├─[send]──────────  provider.SendAsync(request)
        │                   flowTracker?.OnResponseReceived(response)
        │
        └─[classify]─────   RetryClassifier.ClassifyHttpStatus(statusCode)
                            → Succeeded / RetryableFailed / NonRetryableFailed
                              (Consumer dùng để quyết định ACK/retry/DLQ)
```

---

## 8. Nguyên tắc khi làm việc với Application

| Tình huống | Làm gì |
|---|---|
| Thêm loại event mới cần xử lý khác | Thêm use case handler mới trong `UseCases/` |
| Thêm rule validate mới | Thêm vào `EventValidator.Validate()` |
| Thêm provider mới | **Application không cần đổi** — chỉ đăng ký provider mới ở `Providers/` và `Host` |
| Thay đổi logic retry | Sửa `RetryClassifier` |
| Thay đổi convention tên queue | Sửa `ProviderRouter.ResolveQueue()` hoặc cấu hình appsettings |
| **KHÔNG BAO GIỜ** | Import `using PmsIntegration.Infrastructure.*` hay `using PmsIntegration.Host.*` trong Application |
