# Quy ước

> Các quy ước bắt buộc cho Dịch vụ Tích hợp PMS.  
> Tất cả code mới **phải** tuân theo các quy tắc này. Ngoại lệ cần có lý do được ghi lại rõ ràng.

---

## Mục lục

1. [Quy ước đặt tên](#quy-ước-đặt-tên)
2. [Quy tắc ProviderCode](#quy-tắc-providercode)
3. [Đặt tên hàng đợi](#đặt-tên-hàng-đợi)
4. [Correlation ID](#correlation-id)
5. [Trường log](#trường-log)
6. [Quy tắc cấu trúc thư mục](#quy-tắc-cấu-trúc-thư-mục)
7. [Tham chiếu bị cấm](#tham-chiếu-bị-cấm)

---

## Quy ước đặt tên

### Dự án

| Pattern | Ví dụ |
|---|---|
| `PmsIntegration.<Tầng>` | `PmsIntegration.Application` |
| `PmsIntegration.Providers.<Tên>` | `PmsIntegration.Providers.Tiger` |
| `PmsIntegration.<Tầng>.Tests` | `PmsIntegration.Providers.Fake.Tests` |

### Lớp

| Kiểu | Quy ước | Ví dụ |
|---|---|---|
| Interface | Tiền tố `I` + danh từ | `IPmsProvider`, `IQueuePublisher` |
| Use-case handler | `<Hành động>Handler` | `ReceivePmsEventHandler` |
| Options POCO | `<Tên>Options` | `TigerOptions`, `PmsSecurityOptions` |
| Lớp DI extension | `<Tên>ServiceExtensions` | `TigerServiceExtensions` |
| Mapper | `<Provider>Mapper` | `TigerMapper` |
| Request builder | `<Provider>RequestBuilder` | `TigerRequestBuilder` |
| HTTP client wrapper | `<Provider>Client` | `TigerClient` |
| Provider class | `<Provider>Provider` | `TigerProvider` |
| Background service | mô tả hành vi | `ProviderConsumerOrchestrator` |

### File

- Một kiểu public mỗi file.
- Tên file trùng với tên kiểu, ví dụ `TigerProvider.cs`.
- Không dùng hậu tố chung chung như `Helper`, `Util`, hoặc `Manager`.

### Phương thức

- Phương thức async kết thúc bằng `Async`, ví dụ `HandleAsync`, `PublishAsync`, `SendAsync`.
- Phương thức boolean dùng tiền tố `Is`, `Has`, `Can`, hoặc `Try`, ví dụ `TryAcquire`.

---

## Quy tắc ProviderCode

**ProviderCode** (còn gọi là `ProviderKey`) là mã định danh chuẩn cho một nhà cung cấp xuyên suốt toàn hệ thống: config, hàng đợi, message header, và DI.

### Định dạng

- **Chỉ dùng ASCII in hoa**, ví dụ `TIGER`, `OPERA`, `FAKE`
- Các từ cách nhau bằng dấu gạch dưới cho tên nhiều từ, ví dụ `MY_PROVIDER`
- Không có khoảng trắng, dấu gạch ngang, hoặc chữ thường

### Chuẩn hóa

Trước bất kỳ phép so sánh hoặc tra cứu nào, áp dụng:

```csharp
providerCode.Trim().ToUpperInvariant()
```

`IPmsProviderFactory.Get(providerCode)` thực hiện chuẩn hóa này nội bộ, nên caller không cần chuẩn hóa trước khi gọi `Get`.

### Quy tắc nhất quán

ProviderCode phải **giống hệt nhau** ở tất cả các vị trí sau:

| Vị trí | Ví dụ |
|---|---|
| Thuộc tính `IPmsProvider.ProviderKey` | `"TIGER"` |
| `appsettings.json` → `Providers:<KEY>` | `"Providers:TIGER"` |
| `appsettings.json` → `Queues:ProviderQueues:<KEY>` | `"Queues:ProviderQueues:TIGER"` |
| Config section của DI extension | `configuration.GetSection("Providers:TIGER")` |
| Định danh Named `HttpClient` | `"TIGER"` |
| Header tin nhắn RabbitMQ `providerKey` | `"TIGER"` |
| Trường `IntegrationJob.ProviderKey` | `"TIGER"` |

Sự không khớp giữa bất kỳ hai vị trí nào sẽ gây ra `InvalidOperationException` khi chạy từ `PmsProviderFactory.Get`.

---

## Đặt tên hàng đợi

| Loại hàng đợi | Pattern | Ví dụ (Tiger) |
|---|---|---|
| Chính | `q.pms.<providerKeyLower>` | `q.pms.tiger` |
| Retry | `<main>.retry` | `q.pms.tiger.retry` |
| DLQ | `<main>.dlq` | `q.pms.tiger.dlq` |

Quy tắc:
- Provider key trong tên hàng đợi là **chữ thường**.
- Cả ba hàng đợi được khai báo là durable.
- Tên hàng đợi không bao giờ được hard-code; chúng đến từ `Queues:ProviderQueues:<KEY>` trong `appsettings.json`.
- `RabbitMqTopology` tạo tên retry và DLQ bằng cách thêm `.retry` / `.dlq` vào tên hàng đợi chính.

---

## Correlation ID

**Correlation ID** là chuỗi GUID theo dõi một sự kiện PMS duy nhất xuyên suốt toàn pipeline.

### Vòng đời

| Giai đoạn | Hành động |
|---|---|
| PMS gửi sự kiện | `PmsEventEnvelope.CorrelationId` được đặt bởi PMS (hoặc được tạo bởi `ReceivePmsEventHandler` nếu thiếu) |
| HTTP response | Header phản hồi `X-Correlation-Id` được đặt bởi `PmsEventController` |
| `IntegrationJob` | `IntegrationJob.CorrelationId` được sao chép từ envelope |
| Tin nhắn RabbitMQ | Header `correlationId` trên AMQP message |
| Provider HTTP call | Header request `X-Correlation-Id` được đặt trong `ProviderRequest.Headers` bởi `RequestBuilder` của mỗi provider; `CorrelationIdHandler` (trong `Infrastructure/Http/DelegatingHandlers/`) tồn tại cho mục đích này nhưng phải được wired với từng named `HttpClient` qua `.AddHttpMessageHandler<CorrelationIdHandler>()` |
| Tất cả log entries | Luôn bao gồm `correlationId` như một structured field |

### Quy tắc consumer

Khi tin nhắn được chuyển đến hàng đợi `.retry` hoặc `.dlq`, header `correlationId` **phải được giữ nguyên**. Không bao giờ xóa hoặc ghi đè correlation ID.

---

## Trường log

Tất cả log entries có cấu trúc phải bao gồm các trường này. Serilog tự động enrich chúng khi có thể; dùng `LogContext.PushProperty` cho phần còn lại.

### Trường bắt buộc (tất cả log levels)

| Trường | Kiểu | Nguồn |
|---|---|---|
| `correlationId` | `string` (GUID) | `IntegrationJob.CorrelationId` |
| `hotelId` | `string` | `IntegrationJob.HotelId` |
| `eventId` | `string` | `IntegrationJob.EventId` |
| `eventType` | `string` | `IntegrationJob.EventType` |
| `providerKey` | `string` | `IntegrationJob.ProviderKey` |

### Trường tùy chọn (thêm khi có)

| Trường | Kiểu | Khi nào |
|---|---|---|
| `jobId` | `string` (GUID) | Tất cả log consumer/handler |
| `attempt` | `int` | Log retry |
| `x-last-error-code` | `string` | Khi thất bại |
| `x-last-error-message` | `string` | Khi thất bại |

### Audit actions

Chuỗi audit action cố định được log bởi `ElasticAuditLogger`:

| Action | Khi nào |
|---|---|
| `pms.received` | Sự kiện được chấp nhận bởi `PmsEventController` |
| `job.enqueued` | Job được publish lên hàng đợi chính |
| `job.processing` | Consumer bắt đầu xử lý job |
| `job.success` | Lệnh gọi Provider API thành công |
| `job.retryable_failed` | Lệnh gọi Provider API thất bại với kết quả có thể retry (5xx, 408, 429, lỗi mạng) |
| `job.failed` | Lệnh gọi Provider API thất bại với kết quả **không thể retry** (4xx ngoại trừ 408/429, lỗi mapping) |
| `job.provider_not_registered` | `IPmsProviderFactory.Get` không tìm thấy nhà cung cấp cho key |
| `job.duplicate_ignored` | Kiểm tra idempotency từ chối job vì trùng lặp |

> **Lưu ý:** Việc chuyển job vào DLQ được log qua `ILogger` (`LogWarning`) bên trong `ProviderConsumerService`, không qua `IAuditLogger`. Hiện tại không có audit action `job.dlq` nào được emit.

Định dạng audit log:

```
AUDIT {Action} {@Data}
```

> **Lưu ý về tên trường:** `ElasticAuditLogger` dùng tham số template `{Action}`. Serilog serialize tham số này thành thuộc tính `Action` (PascalCase). Các query Kibana trong tài liệu này tham chiếu `auditAction:` — hãy xác minh tên trường thực tế trong Elasticsearch index của bạn khi Elasticsearch sink được wired và đổi tên tham số template thành `{auditAction}` nếu cần cho nhất quán.

Ví dụ:

```json
{
  "level": "Information",
  "message": "AUDIT job.success",
  "correlationId": "3fa85f64-...",
  "hotelId": "H001",
  "eventId": "EVT-001",
  "eventType": "Checkin",
  "providerKey": "TIGER",
  "jobId": "7c9e6679-...",
  "attempt": 1
}
```

### Elasticsearch / Kibana

- Serilog được bootstrap bởi `SerilogElasticSetup.Configure` trong `Infrastructure/Logging/`.
- **Trạng thái hiện tại:** chỉ có **Console** sink đang hoạt động. Lệnh gọi `WriteTo.Elasticsearch(...)` được cung cấp dưới dạng template production được comment-out bên trong `SerilogElasticSetup.cs`. Để bật, thêm `Serilog.Sinks.Elasticsearch` và thay thế khối comment.
- Phiên bản Kibana: **6.8.23**.
- Tất cả log đi vào **một index duy nhất** (khi Elasticsearch được wired). Không tạo index riêng theo provider; dùng trường `providerKey` để lọc.

---

## Quy tắc cấu trúc thư mục

### Host

```
Host/
  Controllers/      → Chỉ là các API endpoint controller
  Middleware/       → Các mối quan tâm cross-cutting của request pipeline
  Background/       → Các triển khai IHostedService (queue consumers)
  Options/          → Các lớp POCO options được bind trong Program.cs
  Providers/        → ProvidersServiceExtensions.cs (điểm composition duy nhất)
  Program.cs
```

**Bị cấm trong Host:** provider-specific mapping, business logic, chi tiết kết nối RabbitMQ.

### Core

```
Core/
  Abstractions/     → Chỉ là interfaces
  Contracts/        → Hình dạng dữ liệu (POCO, không có logic)
  Domain/           → Kiểu giá trị domain (ví dụ IntegrationOutcome)
```

**Bị cấm trong Core:** bất kỳ NuGet package nào tham chiếu RabbitMQ, Serilog, HttpClient, EF, Redis.

### Application

```
Application/
  UseCases/         → ReceivePmsEventHandler, ProcessIntegrationJobHandler
  Services/         → EventValidator, ProviderRouter, RetryClassifier
  DI/               → ApplicationServiceExtensions.cs
```

**Bị cấm trong Application:** sử dụng trực tiếp RabbitMQ hoặc HttpClient; tham chiếu kiểu Infrastructure.

### Infrastructure

```
Infrastructure/
  RabbitMq/         → Connection, topology, publisher, headers
  Logging/          → ElasticAuditLogger, SerilogElasticSetup
  Idempotency/      → InMemoryIdempotencyStore, RedisIdempotencyStore
  Http/
    DelegatingHandlers/  → CorrelationIdHandler
  Config/           → AppSettingsConfigProvider
  Clock/            → SystemClock
  Options/          → RabbitMqOptions, QueueOptions
  Providers/        → PmsProviderFactory
  DI/               → InfrastructureServiceExtensions.cs
```

### Provider

```
Providers.<Tên>/
  <Tên>Options.cs
  <Tên>RequestBuilder.cs
  <Tên>Client.cs
  Mapping/
    <Tên>Mapper.cs
  DI/
    <Tên>ServiceExtensions.cs
```

---

## Tham chiếu bị cấm

Bảng dưới liệt kê các tổ hợp `ProjectReference` **không bao giờ được phép**.

| Từ | KHÔNG được tham chiếu | Lý do |
|---|---|---|
| `Core` | Bất kỳ dự án nào khác trong solution | Core phải không có deps nội bộ |
| `Core` | `RabbitMQ.Client`, `Serilog`, `HttpClient` | Không có infrastructure |
| `Application` | `Infrastructure` | Tầng business không được biết chi tiết triển khai |
| `Application` | Bất kỳ `Providers.*` nào | Application là provider-agnostic |
| `Providers.*` | Bất kỳ `Providers.*` khác | Providers phải độc lập |
| `Providers.*` | `Infrastructure` | Providers không được biết về hàng đợi |
| `Host` | `Core` (dùng business logic) | Host là composition root, không phải tầng business |

### Tóm tắt tham chiếu được phép

```
Host        → Application, Infrastructure, Providers.* (chỉ DI)
Application → Core
Infrastructure → Core, Providers.Abstractions
Providers.* → Providers.Abstractions, Core
Providers.Abstractions → Core
```
