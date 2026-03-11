# Tầng Core — Mô tả chi tiết

> Dành cho team dev — giải thích vai trò, thành phần và lý do tồn tại của `PmsIntegration.Core`

---

## 1. Core là gì?

`PmsIntegration.Core` là **trung tâm** của hệ thống. Đây là tầng duy nhất **không phụ thuộc vào bất kỳ project nào khác** trong solution.

Mọi tầng khác (Application, Infrastructure, Host) đều tham chiếu Core — nhưng Core không tham chiếu ai.

```
Core không biết:
  ❌ RabbitMQ tồn tại
  ❌ Elasticsearch tồn tại
  ❌ ASP.NET tồn tại
  ❌ Redis tồn tại
  ❌ Tiger/Opera API trông như thế nào

Core chỉ biết:
  ✅ Hệ thống nhận sự kiện (event) từ PMS
  ✅ Hệ thống cần gửi job đến một provider nào đó
  ✅ Kết quả có thể là thành công, cần retry, hoặc thất bại hẳn
```

---

## 2. Cấu trúc thư mục

```
PmsIntegration.Core/
├── Abstractions/       ← interface định nghĩa "cần gì", "làm gì"
├── Contracts/          ← DTO truyền dữ liệu giữa các tầng
└── Domain/             ← enum, value object — logic thuần túy
```

---

## 3. Abstractions — Các interface

### 3.1 `IPmsProvider` — Hợp đồng với Provider

**Mục đích:** Định nghĩa một provider (Tiger, Opera, Fake...) phải làm được gì.

```csharp
public interface IPmsProvider
{
    string ProviderKey { get; }  // VD: "TIGER", "OPERA", "FAKE"

    // Bước 1: Nhận job → build request (chỉ mapping, không gọi HTTP)
    Task<ProviderRequest> BuildRequestAsync(IntegrationJob job, CancellationToken ct);

    // Bước 2: Gửi request → nhận response (chỉ I/O, không mapping)
    Task<ProviderResponse> SendAsync(ProviderRequest request, CancellationToken ct);
}
```

**Ai implement:** `TigerProvider`, `OperaProvider`, `FakeProvider` — mỗi provider là một plugin độc lập.

**Tại sao tách `BuildRequestAsync` và `SendAsync`?**
- Cho phép test mapping độc lập với HTTP
- `ProviderFlowTrackerAdapter` có thể capture request body **trước khi gửi** mà không cần hook vào HTTP client

---

### 3.2 `IPmsProviderFactory` — Tra cứu provider theo key

**Mục đích:** Lấy đúng provider dựa trên `providerKey` (string) mà không cần `switch/case` cứng trong code.

```csharp
public interface IPmsProviderFactory
{
    IPmsProvider Get(string providerCode);          // Throws nếu không tìm thấy
    IReadOnlyList<string> RegisteredKeys { get; }   // Danh sách provider đã đăng ký
    IReadOnlyCollection<string> GetRegisteredProviderCodes();
}
```

**Ví dụ dùng trong Application:**
```csharp
// Không cần biết Tiger hay Opera — chỉ cần key từ job
var provider = _providerFactory.Get(job.ProviderKey);
var request  = await provider.BuildRequestAsync(job, ct);
```

**Ai implement:** `PmsProviderFactory` trong `Infrastructure` — tự động discover tất cả `IPmsProvider` đã đăng ký trong DI.

---

### 3.3 `IPmsRequestBuilder` — Build request cho provider

**Mục đích:** Tách riêng bước mapping `IntegrationJob → ProviderRequest` ra khỏi `IPmsProvider`.

```csharp
public interface IPmsRequestBuilder
{
    string ProviderKey { get; }
    Task<ProviderRequest> BuildAsync(IntegrationJob job, CancellationToken ct);
}
```

**Mối quan hệ với `IPmsProvider`:** Provider thường delegate `BuildRequestAsync` cho `IPmsRequestBuilder` nội bộ. Tách ra để dễ test mapping riêng lẻ.

---

### 3.4 `IPmsMapper` — Map payload thuần túy

**Mục đích:** Transform `IntegrationJob.Data` thành JSON string dành riêng cho provider.

```csharp
public interface IPmsMapper
{
    string Map(IntegrationJob job);  // Pure function: không có I/O, không có side-effect
}
```

**Đặc điểm:** Stateless, pure — dễ unit test, không cần mock gì.

---

### 3.5 `IPmsClient` — HTTP transport thuần túy

**Mục đích:** Tách biệt hoàn toàn việc gửi HTTP khỏi việc mapping. Một provider có thể có nhiều client (nhiều endpoint khác nhau).

```csharp
public interface IPmsClient
{
    string ProviderKey { get; }
    Task<ProviderResponse> SendAsync(ProviderRequest request, CancellationToken ct);
}
```

**Ai implement:** `TigerHttpClient`, `OperaHttpClient` — mỗi cái biết endpoint, auth, retry policy riêng.

---

### 3.6 `IIdempotencyStore` — Chống xử lý trùng

**Mục đích:** Đảm bảo mỗi job chỉ được gửi lên provider **đúng 1 lần**, kể cả khi message được deliver nhiều lần (RabbitMQ at-least-once delivery).

```csharp
public interface IIdempotencyStore
{
    // Cố gắng "đặt chỗ" xử lý — trả về false nếu đã có người xử lý trước
    Task<bool> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct);

    // Gia hạn TTL sau khi xử lý thành công (giữ record lâu hơn để chặn duplicate)
    Task MarkSuccessAsync(string key, TimeSpan ttl, CancellationToken ct);
}
```

**Idempotency key format:** `{hotelId}:{eventId}:{eventType}:{providerKey}`

**Ai implement:** `InMemoryIdempotencyStore` (dev/test) hoặc `RedisIdempotencyStore` (production).

---

### 3.7 `IQueuePublisher` — Publish message lên queue

**Mục đích:** Định nghĩa cách đẩy job vào queue — bao gồm main queue, retry queue và DLQ.

```csharp
public interface IQueuePublisher
{
    // Gửi job mới vào main queue
    Task PublishAsync(IntegrationJob job, string queue, CancellationToken ct);

    // Gửi lại vào retry queue (có đính kèm attempt count)
    Task PublishRetryAsync(IntegrationJob job, string retryQueue, int attempt,
        string? errorCode, string? errorMessage, CancellationToken ct);

    // Gửi vào Dead Letter Queue (không thể xử lý được nữa)
    Task PublishDlqAsync(IntegrationJob job, string dlqQueue, int attempt,
        string? errorCode, string? errorMessage, CancellationToken ct);
}
```

**Ai implement:** `RabbitMqQueuePublisher` trong `Infrastructure`.

---

### 3.8 `IAuditLogger` — Ghi audit log

**Mục đích:** Ghi lại các mốc quan trọng trong vòng đời của một job (nhận — enqueue — xử lý — thành công/thất bại).

```csharp
public interface IAuditLogger
{
    void Log(string action, object data);
}
```

**Các action chuẩn:**

| Action | Khi nào |
|---|---|
| `pms.received` | Controller nhận được event từ PMS |
| `job.enqueued` | Job được đẩy vào queue thành công |
| `job.processing` | Consumer bắt đầu xử lý job |
| `job.success` | Provider trả về 2xx |
| `job.failed` | Lỗi không thể retry |
| `job.retryable_failed` | Lỗi tạm thời, sẽ retry |
| `job.duplicate_ignored` | Job đã được xử lý trước đó (idempotency) |
| `job.dlq` | Job bị đưa vào Dead Letter Queue |

**Ai implement:** `ElasticAuditLogger` trong `Infrastructure`.

---

### 3.9 `IClock` — Trừu tượng hóa thời gian

**Mục đích:** Thay vì dùng `DateTime.UtcNow` trực tiếp trong code, dùng `IClock.UtcNow` để có thể kiểm soát thời gian trong test.

```csharp
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
```

**Ai implement:** `SystemClock` (trả về `DateTimeOffset.UtcNow` thật) và `FakeClock` (dùng trong test).

---

### 3.10 `IConfigProvider` — Đọc cấu hình runtime

**Mục đích:** Cho phép đọc cấu hình từ nhiều nguồn (appsettings, database) mà không bind cứng vào `IConfiguration` của ASP.NET.

```csharp
public interface IConfigProvider
{
    string? Get(string key);
    T? GetSection<T>(string sectionKey) where T : class;
}
```

---

### 3.11 `IProviderFlowTracker` — Callback ghi log flow (Adapter Bridge)

**Mục đích đặc biệt:** Cho phép `Application` layer ghi log flow vào Elasticsearch **mà không cần tham chiếu Infrastructure**.

```csharp
public interface IProviderFlowTracker
{
    void OnRequestBuilt(ProviderRequest request);       // Capture request trước khi gửi
    void OnResponseReceived(ProviderResponse response); // Capture response sau khi nhận
}
```

**Cơ chế:** `Host` tạo `ProviderFlowTrackerAdapter` (Infrastructure) implement interface này, rồi truyền vào `ProcessIntegrationJobHandler` (Application). Handler gọi interface, không biết đằng sau là ghi Elasticsearch.

> Xem thêm tại [CLEAN_ARCHITECTURE.vi.md](CLEAN_ARCHITECTURE.vi.md) — Mục 4.

---

## 4. Contracts — Các DTO

### 4.1 `PmsEventEnvelope` — Dữ liệu đầu vào từ PMS

**Hướng đi:** PMS → HTTP POST → Controller

```csharp
public sealed class PmsEventEnvelope
{
    public string HotelId   { get; set; }   // Mã khách sạn
    public string EventId   { get; set; }   // ID duy nhất của sự kiện
    public string EventType { get; set; }   // Loại sự kiện: "CHECK_IN", "CHECK_OUT", ...
    public IReadOnlyList<string> Providers  // Danh sách provider cần gửi: ["TIGER", "OPERA"]
    public string? CorrelationId { get; set; } // ID dùng để trace xuyên suốt
    public JsonElement? Data { get; set; }  // Payload nghiệp vụ (schema tùy provider)
}
```

---

### 4.2 `IntegrationJob` — Đơn vị công việc trong queue

**Hướng đi:** Controller → Queue → Consumer

```csharp
public sealed class IntegrationJob
{
    public string JobId        { get; set; }  // UUID tự sinh
    public string HotelId      { get; set; }
    public string EventId      { get; set; }
    public string EventType    { get; set; }
    public string ProviderKey  { get; set; }  // Chỉ 1 provider mỗi job (đã fan-out)
    public string CorrelationId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public JsonElement? Data   { get; set; }  // Payload gốc từ PmsEventEnvelope
    public string? RawPayload  { get; set; }  // Chỉ dùng cho poison message (không deserialize được)
}
```

**Lưu ý quan trọng:** Một `PmsEventEnvelope` có thể fan-out thành **nhiều `IntegrationJob`** — mỗi job cho một provider riêng biệt.

```
PmsEventEnvelope { Providers: ["TIGER", "OPERA"] }
    │
    ├── IntegrationJob { ProviderKey: "TIGER", JobId: "uuid-1" }  → tiger.queue
    └── IntegrationJob { ProviderKey: "OPERA", JobId: "uuid-2" }  → opera.queue
```

---

### 4.3 `ProviderRequest` — Request gửi lên Provider

**Hướng đi:** `IPmsRequestBuilder.BuildAsync()` → `IPmsClient.SendAsync()`

```csharp
public sealed class ProviderRequest
{
    public string ProviderKey  { get; set; }
    public string CorrelationId { get; set; }
    public string Method       { get; set; }  // "POST"
    public string Endpoint     { get; set; }  // URL đầy đủ
    public string? JsonBody    { get; set; }  // Body đã serialize (JSON hoặc XML)
    public Dictionary<string, string> Headers { get; set; }
}
```

---

### 4.4 `ProviderResponse` — Response nhận từ Provider

**Hướng đi:** `IPmsClient.SendAsync()` → `ProcessIntegrationJobHandler`

```csharp
public sealed class ProviderResponse
{
    public int StatusCode { get; set; }
    public string? Body   { get; set; }
    public bool IsSuccess => StatusCode is >= 200 and < 300;  // Computed property
}
```

---

### 4.5 `IntegrationResult` — Kết quả xử lý job

**Hướng đi:** `ProcessIntegrationJobHandler` → `ProviderConsumerService` (quyết định ACK/retry/DLQ)

```csharp
public sealed class IntegrationResult
{
    public IntegrationOutcome Outcome    { get; }  // Success | RetryableFailure | NonRetryableFailure
    public string? ErrorCode             { get; }
    public string? ErrorMessage          { get; }
    public int? HttpStatusCode           { get; }

    // Factory methods — không dùng constructor trực tiếp
    public static IntegrationResult Succeeded();
    public static IntegrationResult RetryableFailed(string errorCode, string message, int? statusCode);
    public static IntegrationResult NonRetryableFailed(string errorCode, string message, int? statusCode);
}
```

---

## 5. Domain — Logic thuần túy

### 5.1 `IntegrationOutcome` — Enum kết quả

```csharp
public enum IntegrationOutcome
{
    Success,              // Thành công — ACK message
    RetryableFailure,     // Lỗi tạm thời (timeout, 5xx) — đưa vào retry queue
    NonRetryableFailure   // Lỗi vĩnh viễn (400, config lỗi) — đưa vào DLQ
}
```

**Consumer dùng giá trị này để quyết định:**

| Outcome | Hành động |
|---|---|
| `Success` | `BasicAck` |
| `RetryableFailure` | Publish vào `.retry` queue → `BasicAck` |
| `NonRetryableFailure` | Publish vào `.dlq` queue → `BasicAck` |

---

### 5.2 `ProviderKey` — Value Object

**Mục đích:** Đảm bảo provider key luôn **uppercase và không có khoảng trắng** — tránh bug do so sánh string.

```csharp
public sealed class ProviderKey
{
    public string Value { get; }

    public ProviderKey(string raw)
    {
        // Validation tại constructor — không thể tạo ProviderKey không hợp lệ
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("ProviderKey must not be empty.");

        Value = raw.Trim().ToUpperInvariant(); // "tiger " → "TIGER"
    }

    // Implicit conversion → dùng như string bình thường khi cần
    public static implicit operator string(ProviderKey key) => key.Value;
}
```

---

## 6. Sơ đồ tổng quan Core

```
                    ┌─────────────────────────────────────┐
                    │           PmsIntegration.Core         │
                    │                                       │
                    │  Domain/                              │
                    │    IntegrationOutcome (enum)          │
                    │    ProviderKey (value object)         │
                    │                                       │
                    │  Contracts/  (DTO — dữ liệu)         │
                    │    PmsEventEnvelope  ←── từ PMS       │
                    │    IntegrationJob    ←── trong queue  │
                    │    ProviderRequest   ←── gửi provider │
                    │    ProviderResponse  ←── nhận về      │
                    │    IntegrationResult ←── kết quả      │
                    │                                       │
                    │  Abstractions/  (interface — hành vi) │
                    │    IPmsProvider          ┐            │
                    │    IPmsProviderFactory   │ Provider   │
                    │    IPmsRequestBuilder    │ plugin     │
                    │    IPmsMapper            │ system     │
                    │    IPmsClient            ┘            │
                    │    IQueuePublisher   ← queue I/O      │
                    │    IIdempotencyStore ← chống trùng    │
                    │    IAuditLogger      ← audit trail    │
                    │    IClock            ← testable time  │
                    │    IConfigProvider   ← cấu hình       │
                    │    IProviderFlowTracker ← log bridge  │
                    └─────────────────────────────────────┘
                          ↑ tất cả tầng khác phụ thuộc vào đây
```

---

## 7. Nguyên tắc khi làm việc với Core

| Tình huống | Làm gì |
|---|---|
| Thêm loại provider mới | Implement `IPmsProvider` trong `Providers/` — Core **không đổi** |
| Thêm loại event mới | Chỉ thêm field vào `PmsEventEnvelope.Data` (JSON) — Core **không đổi** |
| Đổi Redis → in-memory idempotency | Tạo implementation mới cho `IIdempotencyStore` — Core **không đổi** |
| Thêm logic nghiệp vụ mới | Nếu cần interface mới → thêm vào `Abstractions/`; nếu cần DTO mới → thêm vào `Contracts/` |
| **KHÔNG BAO GIỜ** | Import `using PmsIntegration.Infrastructure.*` hay `using PmsIntegration.Host.*` trong Core |
