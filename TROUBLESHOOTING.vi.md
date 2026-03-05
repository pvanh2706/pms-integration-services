# Xử lý sự cố

> Hướng dẫn chẩn đoán thực tế cho kỹ sư vận hành Dịch vụ Tích hợp PMS.  
> Bắt đầu mọi cuộc điều tra bằng `correlationId` và `providerKey` từ sự kiện thất bại.

---

## Mục lục

1. [Cách tiếp cận chẩn đoán chung](#cách-tiếp-cận-chẩn-đoán-chung)
2. [Provider không được đăng ký](#provider-không-được-đăng-ký)
3. [ProviderCode trùng lặp](#providercode-trùng-lặp)
4. [Lỗi xác thực (401)](#lỗi-xác-thực-401)
5. [Hàng đợi bị kẹt / Tin nhắn không được xử lý](#hàng-đợi-bị-kẹt--tin-nhắn-không-được-xử-lý)
6. [Tin nhắn chuyển thẳng vào DLQ](#tin-nhắn-chuyển-thẳng-vào-dlq)
7. [Lỗi Provider API (4xx / 5xx)](#lỗi-provider-api-4xx--5xx)
8. [Idempotency False Positive (Trùng lặp bị bỏ qua)](#idempotency-false-positive-trùng-lặp-bị-bỏ-qua)
9. [Tài liệu tham khảo truy vấn Kibana](#tài-liệu-tham-khảo-truy-vấn-kibana)

---

## Cách tiếp cận chẩn đoán chung

Mỗi log entry trong dịch vụ này đều chứa các structured field. Cách nhanh nhất để chẩn đoán bất kỳ lỗi nào là:

1. **Lấy `correlationId`** — từ header phản hồi `X-Correlation-Id` hoặc từ log sự kiện PMS.
2. **Lọc log trong Kibana** bằng `correlationId:<value>` để xem toàn bộ trace của pipeline.
3. **Thêm `providerKey`** để thu hẹp range nhà cung cấp nào bị lỗi.
4. **Kiểm tra `auditAction`** để tìm giai đoạn cuối cùng đã biết (`pms.received` → `job.enqueued` → `job.processing` → `job.success` / `job.retryable_failed` / `job.failed`). Các job chuyển sang DLQ được log là `ILogger.LogWarning` (không phải audit action); tìm kiếm `message:*DLQ*`.

Pattern truy vấn Kibana hữu ích:

```
correlationId:"<guid>" AND providerKey:"TIGER"
```

---

## Provider không được đăng ký

### Triệu chứng

```
System.InvalidOperationException: No IPmsProvider registered for provider code 'ACME'. Registered codes: [FAKE, TIGER, OPERA]
```

Exception này được throw bởi `PmsProviderFactory.Get(providerCode)` bên trong `ProcessIntegrationJobHandler`.

### Nguyên nhân và cách sửa

| Nguyên nhân | Cách xác minh | Cách sửa |
|---|---|---|
| `AddAcmeProvider(configuration)` chưa được gọi trong `ProvidersServiceExtensions` | Tìm kiếm `ProvidersServiceExtensions.cs` theo tên provider | Thêm dòng đăng ký |
| `ProjectReference` thiếu trong `Host.csproj` | Chạy `dotnet build` — thiếu reference gây lỗi compile | Thêm `<ProjectReference>` vào `Host.csproj` |
| ProviderCode không khớp giữa `IPmsProvider.ProviderKey` và header tin nhắn `providerKey` | Log giá trị `providerKey` đến và so với `IPmsProvider.ProviderKey` | Căn chỉnh các chuỗi (xem [Quy tắc ProviderCode](CONVENTIONS.vi.md#quy-tắc-providercode)) |
| Provider được đăng ký sau `PmsProviderFactory` trong DI | Factory capture `IEnumerable<IPmsProvider>` tại thời điểm đăng ký | Di chuyển tất cả lệnh gọi `AddXxxProvider()` **trước** `services.AddSingleton<IPmsProviderFactory, PmsProviderFactory>()` |

### Cách liệt kê các provider đã đăng ký khi chạy

`IPmsProviderFactory.RegisteredKeys` trả về tất cả key đã đăng ký. Log điều này khi khởi động hoặc hiển thị qua health-check endpoint.

```json
{
  "registeredProviders": ["FAKE", "TIGER", "OPERA"]
}
```

---

## ProviderCode trùng lặp

### Triệu chứng

Hai provider được đăng ký với cùng `ProviderKey`, ví dụ cả hai đều trả về `"TIGER"`.  
`PmsProviderFactory` sẽ throw tại thời điểm khởi tạo:

```
System.InvalidOperationException: Duplicate provider key 'TIGER' detected. Provider type: PmsIntegration.Providers.Tiger.TigerProvider
```

### Nguyên nhân và cách sửa

| Nguyên nhân | Cách xác minh | Cách sửa |
|---|---|---|
| `AddTigerProvider()` được gọi hai lần trong `ProvidersServiceExtensions` | Đọc `ProvidersServiceExtensions.cs` | Xóa lệnh gọi trùng lặp |
| Hai lớp khác nhau cùng trả về `ProviderKey` giống nhau | Grep tìm chuỗi key trùng lặp trong các dự án `Providers.*` | Đổi tên một provider key và cập nhật tất cả tham chiếu |
| Một dự án provider được đổi tên nhưng đăng ký DI cũ chưa được xóa | Kiểm tra git diff để tìm đăng ký không còn dùng | Xóa đăng ký cũ |

---

## Lỗi xác thực (401)

### Triệu chứng — phía PMS

PMS nhận `HTTP 401 {"error":"unauthorized"}` khi gọi `POST /api/pms/events`.

### Nguyên nhân và cách sửa

| Nguyên nhân | Cách xác minh | Cách sửa |
|---|---|---|
| Thiếu header `X-PMS-TOKEN` | Kiểm tra Kibana tìm `message:"unauthorized"` + đường dẫn request | Đảm bảo PMS gửi header trong mỗi request |
| Giá trị token sai | So sánh PMS token với `PmsSecurity:FixedToken` trong `appsettings.json` | Cập nhật token phía PMS hoặc xoay `PmsSecurity:FixedToken` |
| Tên header sai | Kiểm tra `PmsSecurity:HeaderName` — nên là `X-PMS-TOKEN` trừ khi được ghi đè | Căn chỉnh tên header trong PMS và config dịch vụ |
| Token có khoảng trắng đầu/cuối | Log giá trị header thô (`Request.Headers["X-PMS-TOKEN"]`) | Trim trước khi so sánh — hoặc sửa PMS để gửi giá trị sạch |

### Nơi tìm trong code

`PmsTokenMiddleware.InvokeAsync` trong `src/PmsIntegration.Host/Middleware/PmsTokenMiddleware.cs`.  
Middleware mặc định không log khi thất bại; thêm log `Debug` ở đó trong quá trình điều tra nếu cần.

---

## Hàng đợi bị kẹt / Tin nhắn không được xử lý

### Triệu chứng

Tin nhắn tích lũy trong `q.pms.<provider>` nhưng `job.processing` không bao giờ xuất hiện trong Kibana.

### Các bước chẩn đoán

1. **RabbitMQ có kết nối được không?**

   ```bash
   # Management UI
   http://localhost:15672
   # hoặc
   rabbitmqctl list_queues name messages consumers
   ```

   Nếu `consumers = 0` cho hàng đợi của provider, consumer chưa kết nối.

2. **Tiến trình Host có đang chạy không?**

   Kiểm tra trạng thái dịch vụ hoặc danh sách tiến trình.

3. **`ProviderConsumerOrchestrator` có khởi động tất cả consumer không?**

   Tìm log entry khi khởi động như:

   ```
   Starting consumer for provider TIGER on queue q.pms.tiger
   ```

   Nếu thiếu, provider có thể chưa được đăng ký (xem [Provider không được đăng ký](#provider-không-được-đăng-ký)).

4. **Tên hàng đợi có đúng không?**

   So sánh `Queues:ProviderQueues:TIGER` trong `appsettings.json` với tên hàng đợi hiển thị trong RabbitMQ management UI.

5. **Kết nối RabbitMQ bị ngắt?**

   `RabbitMqConnectionFactory` dùng `AutomaticRecoveryEnabled = true`, nên các ngắt kết nối tạm thời được khôi phục tự động ở cấp connection.

   Ngoài ra, `ProviderConsumerService` có vòng lặp reconnect ngoài: nếu channel đóng bất ngờ sau khi auto-recovery cạn kiệt, service chờ `RabbitMq:ConsumerReconnectDelaySeconds` (mặc định 10 giây) và tự thiết lập lại kết nối và channel. **Không cần restart tiến trình để reconnect.**

   Tìm các log entry này để theo dõi reconnection:
   ```
   Consumer for queue q.pms.tiger lost connection. Reconnecting in 10s.
   Consumer connecting to queue: q.pms.tiger
   Consumer active on queue: q.pms.tiger
   ```

### Reset nhanh (không phải production)

```powershell
# Restart dịch vụ để reconnect consumer
Restart-Service PmsIntegrationService
```

---

## Tin nhắn chuyển thẳng vào DLQ

### Triệu chứng

Sau `MaxRetryAttempts` lần retry, tin nhắn xuất hiện trong `q.pms.<provider>.dlq`.
Tìm các entry `LogWarning` chứa "DLQ" trong log (việc định tuyến DLQ được log qua `ILogger` trong `ProviderConsumerService`, không qua `IAuditLogger`).

### Các bước chẩn đoán

1. **Tìm lỗi cuối cùng** trong Kibana:

   ```
   correlationId:"<guid>" AND (auditAction:"job.failed" OR auditAction:"job.retryable_failed")
   ```

   Kiểm tra các trường `x-last-error-code` và `x-last-error-message`.

2. **Phân loại lỗi:**

   | Loại lỗi | Nguyên nhân có thể |
   |---|---|
   | `5xx` từ provider API | Provider bị sập hoặc quá tải — retry là dự kiến |
   | `4xx` (ví dụ 400, 422) | Lỗi mapping — `IntegrationJob.Data` tạo ra request body không hợp lệ |
   | `401` từ provider API | Thông tin đăng nhập provider API sai hoặc hết hạn |
   | `TaskCanceledException` | Request timeout — tăng `Providers:<KEY>:TimeoutSeconds` |
   | Serialization exception | `IntegrationJob.Data` có định dạng không mong đợi |

3. **Với lỗi mapping:**

   - Kiểm tra `ProviderRequest.Body` được log ở level `DEBUG`.
   - Chạy mapper độc lập với unit test dùng payload thất bại.
   - Sửa mapper và triển khai lại.

4. **Replay tin nhắn DLQ** (thủ công):

   Tin nhắn trong `.dlq` được giữ vô thời hạn. Để replay:
   - Sửa nguyên nhân gốc rễ trước.
   - Dùng RabbitMQ management UI để shovel / chuyển tin nhắn từ `.dlq` về hàng đợi chính.
   - Hoặc publish sự kiện mới từ PMS với cùng payload.

   > **TODO:** Ghi lại script replay nếu project có thêm.

---

## Lỗi Provider API (4xx / 5xx)

### Hành vi retry theo status code

| Status code | Kết quả | Hành động |
|---|---|---|
| 200–299 | `Success` | ACK |
| 408 Request Timeout | `RetryableFailure` | Retry |
| 429 Too Many Requests | `RetryableFailure` | Retry |
| 5xx | `RetryableFailure` | Retry |
| 400 Bad Request | `NonRetryableFailure` | DLQ ngay lập tức |
| 401 Unauthorized | `NonRetryableFailure` | DLQ ngay lập tức |
| 403 Forbidden | `NonRetryableFailure` | DLQ ngay lập tức |
| 404 Not Found | `NonRetryableFailure` | DLQ ngay lập tức |
| 422 Unprocessable Entity | `NonRetryableFailure` | DLQ ngay lập tức |

Logic phân loại nằm trong `Application/Services/RetryClassifier.cs`.

### 401 từ provider API

Provider API từ chối thông tin đăng nhập được lưu trong `Providers:<KEY>:ApiKey` / `ApiSecret` / `ClientId` / `ClientSecret` trong `appsettings.json`.

Các bước:
1. Xoay thông tin đăng nhập trong portal của provider.
2. Cập nhật `appsettings.json` (hoặc secrets manager).
3. Restart dịch vụ.
4. Replay các tin nhắn DLQ bị ảnh hưởng.

### 429 rate limit

Tăng `Queues:RetryDelaySeconds` để làm chậm retry, hoặc liên hệ với provider về hạn mức rate limit.

---

## Idempotency False Positive (Trùng lặp bị bỏ qua)

### Triệu chứng

Một sự kiện mới hợp lệ được log với `auditAction:"job.duplicate_ignored"` mặc dù nó không phải là retry.

### Khóa idempotency

```
{hotelId}:{eventId}:{eventType}:{providerKey}
```

Nếu PMS gửi sự kiện với cùng `hotelId`, `eventId`, và `eventType` trong cửa sổ TTL (mặc định 24 giờ), dịch vụ coi đó là trùng lặp.

### Nguyên nhân và cách sửa

| Nguyên nhân | Cách sửa |
|---|---|
| PMS gửi lại sự kiện với cùng `eventId` cho sự kiện mới hợp lệ | PMS phải dùng `eventId` riêng biệt cho mỗi sự kiện |
| Bug tạo `eventId` phía PMS | Sửa PMS để tạo giá trị `eventId` duy nhất |
| `InMemoryIdempotencyStore` bị xóa do restart, nhưng PMS đang gửi lại retry cũ | Hành vi dự kiến — nếu bản gốc thành công, trùng lặp được bỏ qua đúng cách |
| TTL đặt quá dài | Giảm TTL acquire của `IIdempotencyStore` (cần thay đổi code) |

### Cách kiểm tra

Trong Kibana:

```
eventId:"EVT-001" AND hotelId:"H001" AND auditAction:"job.duplicate_ignored"
```

Cũng tìm kiếm `job.success` gốc để xác nhận sự kiện đã được xử lý đúng vào lần đầu.

---

## Tài liệu tham khảo truy vấn Kibana

Dùng các query này trong Kibana 6.8.23 Discover view với single application index.

| Mục đích | Truy vấn |
|---|---|
| Toàn bộ trace cho một sự kiện | `correlationId:"<guid>"` |
| Tất cả lỗi có thể retry của một provider | `providerKey:"TIGER" AND auditAction:"job.retryable_failed"` |
| Tất cả lỗi không thể retry của một provider | `providerKey:"TIGER" AND auditAction:"job.failed"` |
| Job chuyển sang DLQ | `level:"Warning" AND message:*DLQ*` (định tuyến DLQ được log qua `ILogger.LogWarning`, không qua `IAuditLogger`) |
| Lỗi xác thực vào dịch vụ | `message:"unauthorized"` |
| Sự kiện bị bỏ qua do trùng lặp | `auditAction:"job.duplicate_ignored"` |
| Tất cả sự kiện của một khách sạn | `hotelId:"H001"` |
| Retry cho một job cụ thể | `jobId:"<guid>"` |
| Gọi provider chậm (timeout) | `providerKey:"OPERA" AND auditAction:"job.retryable_failed" AND x-last-error-code:"TIMEOUT"` |
| Provider không được đăng ký | `auditAction:"job.provider_not_registered"` |

**Mẹo:** Ghim `correlationId` như một cột trong Kibana Discover để nhanh chóng quét các dòng log liên quan trong một request duy nhất.
