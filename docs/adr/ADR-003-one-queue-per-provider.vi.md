# ADR-003: Một hàng đợi RabbitMQ mỗi nhà cung cấp

| Trường | Giá trị |
|---|---|
| Trạng thái | Đã chấp nhận |
| Ngày | 2026-03-04 |
| Người quyết định | TODO: ghi lại tên |
| Thay thế | — |

---

## Bối cảnh

Dịch vụ phải xử lý các tin nhắn `IntegrationJob` bất đồng bộ cho nhiều nhà cung cấp (Tiger, Opera, Fake, …).

Cần có quyết định thiết kế về cách phân vùng các hàng đợi tin nhắn:

| Tùy chọn | Mô tả |
|---|---|
| **Một hàng đợi chung** | Tất cả provider chia sẻ một hàng đợi; consumer đọc tin nhắn và dispatch theo `providerKey`. |
| **Một hàng đợi mỗi provider (đã chọn)** | Mỗi provider có hàng đợi chính, hàng đợi retry và dead-letter queue riêng. |
| **Một hàng đợi mỗi loại sự kiện** | Phân vùng theo `eventType` (Checkin, Checkout, …) bất kể provider. |

Các ràng buộc:
- RabbitMQ là message broker.
- Cơ chế retry không được gây ra hot-loop (không dùng `BasicNack(requeue:true)`).
- Độ trễ retry và số lần thử tối đa phải có thể cấu hình.
- Các tin nhắn thất bại phải được giữ lại để kiểm tra thủ công trong DLQ.
- Consumer không cần biết mọi provider tại compile time.

---

## Quyết định

**Mỗi provider được gán ba hàng đợi RabbitMQ riêng:**

| Hàng đợi | Pattern | Mục đích |
|---|---|---|
| Chính | `q.pms.<providerKeyLower>` | Giao tin nhắn thông thường |
| Retry | `q.pms.<providerKeyLower>.retry` | Giao lại với TTL delay về hàng đợi chính |
| DLQ | `q.pms.<providerKeyLower>.dlq` | Tin nhắn không thể khôi phục hoặc hết số lần thử |

### Đặt tên hàng đợi

Tên hàng đợi được cấu hình trong `appsettings.json`. Dịch vụ không bao giờ hard-code chúng:

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

`RabbitMqTopology` tạo tên retry và DLQ bằng cách thêm `.retry` / `.dlq` vào tên hàng đợi chính đã cấu hình.

### Khai báo topology

`RabbitMqTopology.DeclareProviderQueuesAsync(mainQueue)` tạo ba hàng đợi khi khởi động
cho mỗi provider đã đăng ký. Hàng đợi retry sử dụng `x-message-ttl` và `x-dead-letter-routing-key`
để dead-letter trở về hàng đợi chính sau độ trễ đã cấu hình:

```csharp
// RabbitMqTopology.cs (đơn giản hóa)
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

### Điều phối consumer

`ProviderConsumerOrchestrator` (một `IHostedService`) phát hiện tất cả provider key đã đăng ký
từ `IPmsProviderFactory.RegisteredKeys` và khởi động một `ProviderConsumerService` mỗi provider.
Không cần thay đổi `Background/` khi thêm provider mới.

### Luồng ACK / retry / DLQ

Quyết định của consumer dựa trên `IntegrationResult.Outcome`:

```
Success              → ACK
RetryableFailure     → publish lên <main>.retry, tăng x-retry-attempt, ACK bản gốc
                       (sau RetryDelaySeconds tin nhắn trở lại hàng đợi chính qua DLX)
NonRetryableFailure  → publish lên <main>.dlq, ACK bản gốc
attempt ≥ MaxRetryAttempts → publish lên <main>.dlq, ACK bản gốc
```

Bộ đếm `x-retry-attempt` được lưu trong AMQP message header và tăng lên mỗi lần retry.
Các header chẩn đoán bổ sung được thêm khi thất bại:

- `x-last-error-code`
- `x-last-error-message`

**`BasicNack(requeue: true)` bị cấm rõ ràng** — nó re-queue tin nhắn ngay lập tức, tạo ra vòng lặp retry nóng không có back-off.

---

## Các lựa chọn đã xem xét

### A — Một hàng đợi chung

Tất cả provider chia sẻ `q.pms.events`. Consumer đọc `providerKey` từ tin nhắn và dispatch nội bộ.

**Bị từ chối vì:**
- Provider chậm hoặc lỗi (ví dụ Tiger bị sập) chặn xử lý tin nhắn của tất cả provider khác đứng sau trong hàng đợi — head-of-line blocking.
- Không thể điều chỉnh retry delay hoặc max attempts theo từng provider.
- Throughput consumer của một provider bị throttle bởi provider chậm nhất.
- DLQ sẽ tích lũy tin nhắn từ tất cả provider, làm cho chẩn đoán khó hơn.

### B — Một hàng đợi mỗi loại sự kiện

Hàng đợi riêng cho `Checkin`, `Checkout`, v.v., bất kể provider.

**Bị từ chối vì:**
- Bottleneck là provider API, không phải loại sự kiện.
- Một loại sự kiện đơn (ví dụ Checkin) gửi đến Tiger và Opera vẫn cần tách thành các tin nhắn per-provider — cùng vấn đề như hàng đợi chung, chỉ đổi tên.

### C — Một hàng đợi mỗi provider mỗi loại sự kiện

Chi tiết: `q.pms.tiger.checkin`, `q.pms.tiger.checkout`, v.v.

**Bị từ chối vì:**
- Bùng nổ hàng đợi theo cấp số nhân khi provider và loại sự kiện tăng lên.
- Không có lợi ích có ý nghĩa so với one-queue-per-provider vì hành vi retry là per-provider, không phải per-event-type.

---

## Hệ quả

### Tích cực

- **Cô lập:** Tiger bị sập không làm chậm xử lý Opera.
- **Điều chỉnh độc lập:** `RetryDelaySeconds` và `MaxRetryAttempts` áp dụng toàn cục hiện tại; cấu trúc config (`Queues:ProviderQueues`) giúp ghi đè per-provider đơn giản trong tương lai mà không cần thiết kế lại topology.
- **Không có hot-loop:** Hàng đợi retry dựa trên TTL đảm bảo back-off tối thiểu bằng `RetryDelaySeconds` trước khi giao lại.
- **Consumer growth zero-code:** `ProviderConsumerOrchestrator` phát hiện hàng đợi từ `IPmsProviderFactory.RegisteredKeys` — thêm provider tự động khởi động consumer mới.
- **DLQ cô lập:** Tin nhắn Tiger không thể khôi phục nằm trong `q.pms.tiger.dlq`, không trộn lẫn với Opera.

### Tiêu cực / Đánh đổi

- Mỗi provider mới tạo thêm ba hàng đợi RabbitMQ. Với số lượng provider lớn điều này có thể trở thành vấn đề quản lý, nhưng không phải vấn đề kỹ thuật về khả năng mở rộng cho RabbitMQ.
- `RetryDelaySeconds` và `MaxRetryAttempts` hiện tại là toàn cục. Cấu hình delay per-provider đòi hỏi thay đổi schema của `QueueOptions`.
- Replay DLQ là thủ công (RabbitMQ management UI shovel hoặc script tùy chỉnh). TODO: tiện ích replay chưa được triển khai.

---

## Cách triển khai

Khi thêm provider mới:

1. Thêm tên hàng đợi vào `appsettings.json`:
   ```json
   "Queues": {
     "ProviderQueues": {
       "ACME": "q.pms.acme"
     }
   }
   ```
2. Đăng ký provider trong `ProvidersServiceExtensions` (xem ADR-001). `RabbitMqTopology` và `ProviderConsumerOrchestrator` tự động nhận hàng đợi mới từ `IPmsProviderFactory.RegisteredKeys`.
3. Không có thay đổi nào cho `RabbitMqTopology`, `ProviderConsumerOrchestrator`, hay `ProviderConsumerService`.

---

## Cách xác nhận

1. **Khai báo hàng đợi:** sau khi khởi động, RabbitMQ management UI (http://localhost:15672) phải hiển thị tất cả ba hàng đợi mỗi provider (`q.pms.acme`, `q.pms.acme.retry`, `q.pms.acme.dlq`).
2. **Luồng retry:** đặt `FakeOptions:SimulateFailure = true`, publish job vào `q.pms.fake`; xác minh tin nhắn xuất hiện trong `q.pms.fake.retry` và sau đó trở về `q.pms.fake` sau `RetryDelaySeconds`, lặp lại cho đến khi `MaxRetryAttempts` đạt, rồi kết thúc trong `q.pms.fake.dlq`.
3. **Cô lập:** làm kẹt consumer của Tiger (`SimulateFailure`) và xác minh sự kiện Opera tiếp tục xử lý không có độ trễ.
4. **Không có requeue:** tìm kiếm codebase tìm `BasicNack` với `requeue: true`; kết quả phải trống.
5. **Header x-retry-attempt:** kiểm tra tin nhắn trong `q.pms.fake.retry` qua management UI; header `x-retry-attempt` phải có mặt và tăng lên mỗi lần retry.
6. **Startup log:** `ProviderConsumerOrchestrator` phải log một dòng `"Starting consumer for provider X"` mỗi provider đã đăng ký.
