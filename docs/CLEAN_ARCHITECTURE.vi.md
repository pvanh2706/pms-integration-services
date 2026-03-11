# Clean Architecture — Giải thích chi tiết (Tiếng Việt)

## 1. Tổng quan

Clean Architecture (CA) là một cách tổ chức code theo các **tầng đồng tâm**, trong đó:
- Tầng trong cùng (Core/Domain) chứa logic nghiệp vụ thuần túy
- Tầng ngoài (Infrastructure, Host) chứa các chi tiết kỹ thuật như database, HTTP, message queue
- **Quy tắc bất biến**: dependency chỉ được trỏ vào trong, không bao giờ ra ngoài

```
┌────────────────────────────────────────────────────┐
│  Host  (ASP.NET, Background Services)              │
│  ┌──────────────────────────────────────────────┐  │
│  │  Infrastructure  (RabbitMQ, Elasticsearch,   │  │
│  │                   HTTP clients, Logging)      │  │
│  │  ┌────────────────────────────────────────┐  │  │
│  │  │  Application  (Use Cases, Services)    │  │  │
│  │  │  ┌──────────────────────────────────┐  │  │  │
│  │  │  │  Core  (Domain, Contracts,       │  │  │  │
│  │  │  │         Abstractions)            │  │  │  │
│  │  │  └──────────────────────────────────┘  │  │  │
│  │  └────────────────────────────────────────┘  │  │
│  └──────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────┘

Mũi tên dependency hợp lệ:  Host → Infrastructure → Application → Core
```

---

## 2. Các tầng trong project này

### 2.1 Core (`PmsIntegration.Core`)

**Là gì:** Trái tim của hệ thống. Chứa logic nghiệp vụ và các định nghĩa trừu tượng.

**Chứa gì:**
- `Domain/` — entity, value object, logic thuần túy
- `Contracts/` — DTO truyền dữ liệu giữa các tầng (`IntegrationJob`, `PmsEventEnvelope`, `ProviderRequest`, `ProviderResponse`)
- `Abstractions/` — interface mà các tầng ngoài phải implement (`IPmsProvider`, `IIdempotencyStore`, `IProviderFlowTracker`)

**Quy tắc:**
- ✅ Không tham chiếu bất kỳ project nào khác
- ✅ Không biết RabbitMQ, Elasticsearch, HTTP tồn tại
- ✅ Không biết ASP.NET tồn tại
- ✅ Có thể dùng lại trong bất kỳ loại ứng dụng nào (console, web, worker)

---

### 2.2 Application (`PmsIntegration.Application`)

**Là gì:** Tầng điều phối use case. Orchestrate luồng xử lý nhưng không chứa logic kỹ thuật.

**Chứa gì:**
- `UseCases/` — handler xử lý từng use case (`ProcessIntegrationJobHandler`, `ReceivePmsEventHandler`)
- `Services/` — service hỗ trợ use case (`EventValidator`, `RetryClassifier`)

**Quy tắc:**
- ✅ Chỉ tham chiếu `Core`
- ❌ Không tham chiếu `Infrastructure`
- ❌ Không tham chiếu `Host`
- ✅ Chỉ dùng interface từ `Core`, không biết implementation cụ thể

**Ví dụ đúng:**
```csharp
// ProcessIntegrationJobHandler.cs — chỉ dùng interface từ Core
public sealed class ProcessIntegrationJobHandler
{
    private readonly IPmsProviderFactory _providerFactory; // interface từ Core
    private readonly IIdempotencyStore   _idempotency;     // interface từ Core
    private readonly IAuditLogger        _audit;           // interface từ Core
}
```

---

### 2.3 Infrastructure (`PmsIntegration.Infrastructure`)

**Là gì:** Tầng kỹ thuật. Implement các interface từ Core bằng các công nghệ cụ thể.

**Chứa gì:**
- `RabbitMq/` — kết nối, publish, topology
- `Logging/` — Serilog, Elasticsearch sink, flow logger
- `Idempotency/` — Redis hoặc in-memory store
- `Http/` — HTTP client wrapper
- `Options/` — cấu hình cho infrastructure

**Quy tắc:**
- ✅ Tham chiếu `Core` và `Application`
- ❌ Không tham chiếu `Host`
- ✅ Implement interface từ Core (ví dụ: `IIdempotencyStore` → `RedisIdempotencyStore`)

---

### 2.4 Host (`PmsIntegration.Host`)

**Là gì:** Entry point. Khởi động ứng dụng, wiring DI, expose HTTP endpoint.

**Chứa gì:**
- `Program.cs` — đăng ký DI, cấu hình pipeline
- `Controllers/` — ASP.NET controller, nhận HTTP request
- `Background/` — hosted service xử lý queue
- `Middleware/` — xác thực token, error handling
- `Options/` — cấu hình riêng cho HTTP layer (`PmsSecurityOptions`)

**Quy tắc:**
- ✅ Tham chiếu tất cả các tầng khác
- ✅ Là nơi duy nhất biết class nào implement interface nào
- ✅ Wiring DI: `services.AddScoped<IIdempotencyStore, RedisIdempotencyStore>()`

---

## 3. Dependency Rule — Quy tắc trung tâm

```
ĐƯỢC PHÉP:
Host           → Infrastructure  ✅
Host           → Application     ✅
Host           → Core            ✅
Infrastructure → Application     ✅
Infrastructure → Core            ✅
Application    → Core            ✅

BỊ CẤM:
Core           → bất kỳ tầng nào  ❌
Application    → Infrastructure   ❌
Application    → Host             ❌
Infrastructure → Host             ❌
```

---

## 4. Vấn đề thực tế: khi Application cần Infrastructure

Đây là tình huống hay gặp nhất. Application cần gọi một thứ mà chỉ Infrastructure mới có.

**Tình huống cụ thể trong project:** `ProcessIntegrationJobHandler` (Application) cần ghi log flow khi gọi Provider — nhưng log được implement trong `IProviderFlowLogger` (Infrastructure).

**Sai — vi phạm CA:**
```csharp
// ProcessIntegrationJobHandler.cs (Application)
using PmsIntegration.Infrastructure.Logging.Flow; // ❌ Application tham chiếu Infrastructure

public class ProcessIntegrationJobHandler
{
    private readonly IProviderFlowLogger _logger; // ❌ Infrastructure type
}
```

**Đúng — dùng Dependency Inversion + Adapter Pattern:**

**Bước 1 — Core định nghĩa interface đơn giản:**
```csharp
// Core/Abstractions/IProviderFlowTracker.cs
public interface IProviderFlowTracker
{
    void OnRequestBuilt(ProviderRequest request);
    void OnResponseReceived(ProviderResponse response);
}
```

**Bước 2 — Application chỉ dùng interface từ Core:**
```csharp
// Application/UseCases/ProcessIntegrationJobHandler.cs
public async Task<IntegrationResult> HandleAsync(
    IntegrationJob job,
    IProviderFlowTracker? flowTracker = null) // ✅ interface từ Core, không biết implementation
{
    var request = await provider.BuildRequestAsync(job, ct);
    flowTracker?.OnRequestBuilt(request); // ✅ gọi interface, không biết class cụ thể
    ...
}
```

**Bước 3 — Infrastructure tạo Adapter implement interface:**
```csharp
// Infrastructure/Logging/Flow/ProviderFlowTrackerAdapter.cs
public sealed class ProviderFlowTrackerAdapter : IProviderFlowTracker // ✅ implement Core interface
{
    private readonly IProviderFlowLogger _flowLogger; // ✅ Infrastructure type — nằm đúng tầng

    public void OnRequestBuilt(ProviderRequest request)
    {
        // Dịch từ Core callback → Infrastructure log call
        _flowLogger.SetProviderRequest(...);
        _flowLogger.Step(ProviderFlowStep.HttpSending, ...);
    }

    public void OnResponseReceived(ProviderResponse response)
    {
        _flowLogger.SetProviderResponse(...);
        _flowLogger.Step(ProviderFlowStep.HttpResponseReceived, ...);
    }
}
```

**Bước 4 — Host wiring tất cả lại:**
```csharp
// Host/Background/ProviderConsumerService.cs
var providerLogger = scope.ServiceProvider.GetRequiredService<IProviderFlowLogger>();
var flowTracker    = new ProviderFlowTrackerAdapter(providerLogger); // ✅ Host biết cả hai đầu
await handler.HandleAsync(job, flowTracker, ct);
```

---

## 5. Luồng dependency đầy đủ trong project

```
PmsEventController (Host)
    │ inject ReceivePmsEventHandler     (Application)
    │ inject IApiFlowLogger             (Infrastructure — được phép vì Host → Infrastructure)
    ▼
ReceivePmsEventHandler (Application)
    │ inject IPmsProviderFactory        (interface từ Core → impl ở Infrastructure)
    │ inject IQueuePublisher            (interface từ Core → impl ở Infrastructure)
    ▼
ProviderConsumerService (Host)
    │ resolve IProviderFlowLogger       (Infrastructure)
    │ tạo    ProviderFlowTrackerAdapter (Infrastructure)
    │ resolve ProcessIntegrationJobHandler (Application)
    ▼
ProcessIntegrationJobHandler (Application)
    │ nhận IProviderFlowTracker         (interface từ Core)
    │ → thực ra là ProviderFlowTrackerAdapter — nhưng handler KHÔNG biết điều đó ✅
    ▼
ProviderFlowTrackerAdapter (Infrastructure)
    │ gọi IProviderFlowLogger
    ▼
ProviderFlowLogger (Infrastructure)
    │ gọi ILogger<T> → Serilog
    ▼
Serilog Sink → Elasticsearch
```

---

## 6. Tại sao `Options` có ở cả `Host` lẫn `Infrastructure`?

Đây không phải trùng lặp — mỗi loại option phục vụ một tầng khác nhau:

| Options | Tầng | Lý do |
|---|---|---|
| `ElasticOptions`, `RabbitMqOptions`, `QueueOptions` | `Infrastructure` | Cấu hình kết nối đến external system — concern của Infrastructure |
| `PmsSecurityOptions` | `Host` | Cấu hình HTTP middleware (token, replay window) — concern của HTTP pipeline, không liên quan đến Infrastructure |

Nếu sau này thay Host bằng gRPC hoặc worker service thuần thì `PmsSecurityOptions` không còn ý nghĩa, còn `RabbitMqOptions` vẫn dùng được.

---

## 7. Lợi ích thực tế

| Lợi ích | Ví dụ trong project |
|---|---|
| **Testable** | Test `ProcessIntegrationJobHandler` không cần RabbitMQ hay Elasticsearch thật — chỉ cần mock `IPmsProviderFactory`, `IIdempotencyStore` |
| **Swappable** | Đổi Redis → in-memory idempotency store chỉ cần thay 1 dòng DI trong `Program.cs` |
| **Tách biệt concern** | Logic nghiệp vụ trong `Core` và `Application` không thay đổi khi thay đổi infrastructure |
| **Rõ ràng trách nhiệm** | Nhìn vào namespace biết ngay layer nào chịu trách nhiệm gì |
| **Dễ onboard** | Developer mới chỉ cần đọc `Core` và `Application` để hiểu business, không cần hiểu RabbitMQ hay Elasticsearch |
