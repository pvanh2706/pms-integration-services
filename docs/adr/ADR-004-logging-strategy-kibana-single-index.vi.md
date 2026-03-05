# ADR-004: Chiến lược Logging — Serilog, Elasticsearch Index Đơn, Kibana 6.8.23

| Trường | Giá trị |
|---|---|
| Trạng thái | Đã chấp nhận |
| Ngày | 2026-03-04 |
| Người quyết định | TODO: ghi lại tên |
| Thay thế | — |
| Ràng buộc | Phiên bản Kibana cố định ở **6.8.23** và không thể nâng cấp. |

---

## Bối cảnh

Dịch vụ phải tạo ra các log có cấu trúc, có thể truy vấn cho:

1. **Chẩn đoán vận hành** — theo dõi sự kiện PMS qua toàn bộ pipeline bằng `correlationId`.
2. **Audit trail** — ghi lại các sự kiện audit riêng biệt (`pms.received`, `job.success`, `job.dlq`, v.v.) cho mỗi `IntegrationJob`.
3. **Phân tích lỗi** — hiển thị provider key, HTTP status và error message khi job thất bại hoặc được retry.

Ràng buộc:
- Stack Elasticsearch / Kibana cố định ở **Kibana 6.8.23**.
- Kibana 6.x không hỗ trợ ILM index rollover trong UI. Một index pattern đơn đơn giản hóa việc quản lý.
- Team vận hành một môi trường triển khai mỗi giai đoạn (dev, staging, production). Các index per-provider hoặc per-event-type thêm overhead vận hành mà không có lợi ích tương xứng.
- Log shipping không được là bottleneck trên đường xử lý sự kiện.

---

## Quyết định

### 1. Serilog là logging framework

`Serilog` là thư viện structured logging xuyên suốt solution.

- Cấu hình được tập trung trong `SerilogElasticSetup.Configure` (trong `PmsIntegration.Infrastructure/Logging/`).
- **Triển khai hiện tại:** `SerilogElasticSetup.Configure` chỉ wires **Console** sink. Lệnh gọi `WriteTo.Elasticsearch(...)` được cung cấp dưới dạng template production được comment-out. Bật bằng cách thêm `Serilog.Sinks.Elasticsearch` và thay thế khối comment.
- Host bootstrap Serilog trước khi DI container được build:

  ```csharp
  // Program.cs
  Log.Logger = SerilogElasticSetup
      .Configure(new LoggerConfiguration(), builder.Configuration)
      .CreateLogger();
  builder.Host.UseSerilog();
  ```

- Tất cả tầng khác sử dụng `Microsoft.Extensions.Logging.ILogger<T>` được inject bởi DI; Serilog thực thi abstraction đó khi chạy qua `UseSerilog()`.
- `PmsIntegration.Core` và `PmsIntegration.Application` **không được** tham chiếu Serilog trực tiếp — chúng chỉ log qua `ILogger<T>`.

### 2. Một Elasticsearch index duy nhất cho tất cả log entries

Tất cả log entries (tất cả tầng, tất cả provider) được ghi vào một index duy nhất. Tên index được cấu hình trong `appsettings.json`.

> **TODO:** Ghi lại config key chính xác khi tham số index format của `SerilogElasticSetup` được xác nhận (dự kiến: `Serilog:WriteTo:0:Args:indexFormat` hoặc tương tự).

**Tại sao một index duy nhất:**
- Quản lý index pattern Kibana 6.x là thủ công. Nhiều index đòi hỏi nhiều index pattern và các tab Discover riêng biệt.
- Correlation giữa các provider (ví dụ theo dõi một sự kiện PMS fan-out đến Tiger và Opera) đòi hỏi cả hai log trong cùng query context.
- Lọc provider được thực hiện qua trường structured `providerKey` — không cần tách index.

### 3. Các trường log structured bắt buộc

Tất cả log entries — bất kể log level — phải bao gồm các trường này như Serilog properties (dùng `LogContext.PushProperty` hoặc message template destructuring):

| Trường | Kiểu | Mô tả |
|---|---|---|
| `correlationId` | `string` (GUID) | Theo dõi một sự kiện PMS từ đầu đến cuối |
| `hotelId` | `string` | Định danh property/khách sạn từ sự kiện |
| `eventId` | `string` | Định danh sự kiện duy nhất từ PMS |
| `eventType` | `string` | ví dụ `Checkin`, `Checkout` |
| `providerKey` | `string` | Provider code in hoa, ví dụ `TIGER` |
| `jobId` | `string` (GUID) | Định danh `IntegrationJob` (đặt khi có) |
| `attempt` | `int` | Số lần retry (đặt khi có) |

### 4. Sự kiện audit qua ElasticAuditLogger

Các thời điểm quan trọng trong domain của pipeline được ghi lại dưới dạng **sự kiện audit** bởi `ElasticAuditLogger` (triển khai `IAuditLogger` trong `Infrastructure/Logging/`).

Chuỗi audit action cố định:

| `auditAction` | Khi nào |
|---|---|
| `pms.received` | Sự kiện được chấp nhận bởi `PmsEventController` |
| `job.enqueued` | `IntegrationJob` được publish lên provider queue |
| `job.processing` | Consumer nhận job và bắt đầu xử lý |
| `job.success` | Lệnh gọi Provider API trả về phản hồi thành công |
| `job.retryable_failed` | Lệnh gọi Provider API thất bại với kết quả có thể retry (5xx, 408, 429, timeout, lỗi mạng) |
| `job.failed` | Lệnh gọi Provider API thất bại với kết quả **không thể retry** (4xx ngoại trừ 408/429, lỗi mapping) |
| `job.provider_not_registered` | `IPmsProviderFactory.Get` không tìm thấy provider cho key |
| `job.duplicate_ignored` | Kiểm tra idempotency từ chối job vì trùng lặp |

> **Lưu ý:** Chuyển job sang DLQ được log qua `ILogger.LogWarning` trong `ProviderConsumerService`, không qua `IAuditLogger`. Hiện tại không có audit action `job.dlq` nào được emit.

Định dạng log:

```
AUDIT {auditAction} {@Data}
```

Ví dụ JSON đã render trong Elasticsearch:

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

### 5. Truyền CorrelationId

`correlationId` phải xuất hiện trong mỗi dòng log liên quan đến một job:

| Giai đoạn | Cơ chế |
|---|---|
| HTTP đến | `ReceivePmsEventHandler` tạo hoặc kế thừa `CorrelationId` từ `PmsEventEnvelope` |
| HTTP response | `PmsEventController` đặt header phản hồi `X-Correlation-Id` |
| `IntegrationJob` | `IntegrationJob.CorrelationId` mang theo |
| Tin nhắn RabbitMQ | Header AMQP `correlationId` |
| Provider HTTP request | `CorrelationIdHandler` (delegating handler) có thể thêm `X-Correlation-Id` vào lệnh gọi đi ra; phải được wired với từng named `HttpClient` qua `.AddHttpMessageHandler<CorrelationIdHandler>()` trong DI extension của provider. Providers cũng có thể đặt `X-Correlation-Id` trực tiếp trong `ProviderRequest.Headers`. |
| Consumer log scope | `LogContext.PushProperty("correlationId", ...)` bên trong consumer |

---

## Các lựa chọn đã xem xét

### A — Per-provider index (`pms-tiger-*`, `pms-opera-*`)

Mỗi provider ghi vào index riêng.

**Bị từ chối vì:**
- Truy vấn correlation cross-provider đòi hỏi Kibana multi-index patterns, không thuận tiện trong Kibana 6.x.
- Sự kiện PMS fan-out đến nhiều provider; theo dõi từ đầu đến cuối đòi hỏi một query duy nhất.
- Thêm overhead quản lý index mà không có lợi ích ở quy mô hiện tại.

### B — Per-component index (`pms-host-*`, `pms-application-*`)

Mỗi tầng kiến trúc ghi vào index riêng.

**Bị từ chối vì:**
- Theo dõi một request đơn qua nhiều index — ngược lại với những gì kỹ sư cần khi chẩn đoán sự cố.
- Kibana 6.x không hỗ trợ cross-index correlation natively.

### C — Application Insights / OpenTelemetry

Thay Serilog + Elasticsearch bằng distributed tracing platform.

**Bị từ chối vì:**
- Stack Kibana 6.8.23 là ràng buộc vận hành hiện có không thể thay đổi.
- OpenTelemetry / Application Insights đòi hỏi thay đổi infrastructure nằm ngoài phạm vi hiện tại của team.
- Có thể được xem xét lại trong ADR tương lai nếu ràng buộc Kibana được dỡ bỏ.

### D — Audit log riêng vào cơ sở dữ liệu quan hệ

Ghi sự kiện `IAuditLogger` vào SQL ngoài (hoặc thay thế) Elasticsearch.

**Bị từ chối vì:**
- Thêm synchronous write dependency trên hot processing path.
- Elasticsearch đã cung cấp structured storage có thể truy vấn đáp ứng yêu cầu audit.
- TODO: đánh giá xem SQL audit trail có được yêu cầu cho mục đích tuân thủ không.

---

## Hệ quả

### Tích cực

- Index pattern Kibana đơn bao phủ toàn bộ pipeline — một tab Discover cho tất cả chẩn đoán.
- `correlationId` như một trường nhất quán cho phép pivot một cú nhấp từ bất kỳ dòng log nào đến tất cả dòng liên quan.
- `auditAction` như từ vựng cố định ngăn chặn phân mảnh audit log free-text.
- Serilog `LogContext` enrichment giữ việc điền trường ra ngoài business code; handlers push properties vào context xung quanh.
- Tách biệt: `Application` và `Core` sử dụng `ILogger<T>` — hoán đổi Serilog bằng provider khác chỉ đòi hỏi thay đổi trong `Infrastructure` và `Host`.

### Tiêu cực / Đánh đổi

- Một index duy nhất phát triển không giới hạn mà không có ILM policy (Kibana 6.x không có ILM UI). Retention index phải được quản lý qua Elasticsearch curator hoặc cron job. TODO: ghi lại chiến lược retention/rollover.
- Tất cả log level (Debug, Info, Warning, Error) đến cùng một index. Debug logging khối lượng cao trong production sẽ làm phình index size. Đặt log level `Default` thành `Information` trong production (`appsettings.json` Logging:LogLevel:Default).
- Nếu `SerilogElasticSetup` mất kết nối đến Elasticsearch, log bị bỏ trừ khi có fallback sink (ví dụ file) được cấu hình. TODO: thêm file sink làm fallback cho production.

---

## Cách triển khai

1. `SerilogElasticSetup.Configure` (trong `Infrastructure/Logging/`) phải:
   - Đọc Elasticsearch URL và index format từ `appsettings.json`.
   - Cấu hình `Enrich.FromLogContext()` để trường `LogContext.PushProperty` xuất hiện trên mỗi event.
   - TODO: ghi lại và xác minh đường dẫn config key chính xác.

2. Trong mỗi consumer và handler, push các trường bắt buộc vào `LogContext`:

   ```csharp
   using (LogContext.PushProperty("correlationId", job.CorrelationId))
   using (LogContext.PushProperty("providerKey",   job.ProviderKey))
   using (LogContext.PushProperty("jobId",          job.JobId))
   using (LogContext.PushProperty("attempt",        attempt))
   {
       // code xử lý ở đây
   }
   ```

3. `ElasticAuditLogger.Log(auditAction, data)` phải dùng `Serilog.Log.ForContext(...)` với thuộc tính `auditAction` cố định để bật lọc Kibana.

4. Không bao giờ gọi `Serilog.Log.*` hoặc `ILogger<T>` trực tiếp từ `Core`. Dùng `IAuditLogger` (inject qua DI) cho sự kiện audit trong `Application`.

---

## Cách xác nhận

1. **Kibana index pattern:** sau khi khởi động dịch vụ và gửi một sự kiện, mở Kibana → Management → Index Patterns; xác minh index đã cấu hình tồn tại với `@timestamp` là time field.

2. **Trường bắt buộc:** chạy query này trong Kibana Discover; tất cả kết quả phải có tất cả trường bắt buộc được điền:

   ```
   auditAction:"job.success"
   ```

   Kiểm tra `correlationId`, `hotelId`, `eventId`, `eventType`, `providerKey`, `jobId` và `attempt` đều không null.

3. **Truy vấn full trace:** lấy `X-Correlation-Id` từ phản hồi sự kiện test và chạy:

   ```
   correlationId:"<guid>"
   ```

   Kết quả nên bao gồm: `pms.received`, `job.enqueued`, `job.processing`, `job.success` (theo thứ tự đó theo `@timestamp`).

4. **Từ vựng audit:** `grep -rn "AUDIT " src/` — tất cả log site phải chỉ dùng các chuỗi `auditAction` cố định được liệt kê trong ADR này.

5. **Không có Serilog trong Core/Application:** `grep -rn "using Serilog" src/PmsIntegration.Core src/PmsIntegration.Application` phải không trả về kết quả.

6. **Cô lập provider trong Kibana:** lọc theo `providerKey:"TIGER"` và xác nhận chỉ các dòng log liên quan đến Tiger xuất hiện.
