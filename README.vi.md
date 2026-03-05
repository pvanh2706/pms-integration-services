# Dịch vụ Tích hợp PMS

Một cổng tích hợp kết nối **Hệ thống Quản lý Khách sạn (PMS)** với nhiều **API nhà cung cấp** bên thứ ba (Tiger, Opera, Fake) thông qua RabbitMQ.

Dịch vụ nhận các sự kiện HTTP từ PMS, phân phối mỗi sự kiện vào hàng đợi RabbitMQ riêng theo từng nhà cung cấp, và xử lý từng hàng đợi một cách độc lập — với cơ chế thử lại và xử lý thư chết được tích hợp sẵn.

---

## Mục lục

1. [Bắt đầu nhanh](#bắt-đầu-nhanh)
2. [Chạy Host](#chạy-host)
3. [Cấu hình tối thiểu](#cấu-hình-tối-thiểu)
4. [Cấu trúc dự án](#cấu-trúc-dự-án)
5. [Đọc thêm](#đọc-thêm)

---

## Bắt đầu nhanh

### Yêu cầu

| Công cụ | Phiên bản tối thiểu |
|------|----------------|
| .NET SDK | 10.0 |
| RabbitMQ | 3.12+ |
| Elasticsearch | 6.8.23 (Kibana 6.8.23) — **chỉ dùng cho production**; không bắt buộc khi chạy cục bộ (ghi log ra console là mặc định) |

### 1 — Clone và khôi phục phụ thuộc

```bash
git clone <repo-url>
cd pms-integration-service
dotnet restore PmsIntegration.sln
```

### 2 — Khởi động RabbitMQ (Docker)

```bash
docker run -d --name rabbitmq \
  -p 5672:5672 -p 15672:15672 \
  rabbitmq:3-management
```

Giao diện quản lý → http://localhost:15672 (guest / guest)

### 3 — Thiết lập token bảo mật

Mở `src/PmsIntegration.Host/appsettings.Development.json` và đặt:

```json
{
  "PmsSecurity": {
    "FixedToken": "my-local-dev-token"
  }
}
```

### 4 — Build và chạy

```bash
dotnet build PmsIntegration.sln
dotnet run --project src/PmsIntegration.Host/PmsIntegration.Host.csproj
```

API khả dụng tại `https://localhost:<port>`.  
OpenAPI được hiển thị tại `/openapi` trong môi trường Development.

### 5 — Gửi sự kiện thử nghiệm

```bash
curl -X POST https://localhost:<port>/api/pms/events \
  -H "Content-Type: application/json" \
  -H "X-PMS-TOKEN: my-local-dev-token" \
  -d '{
    "hotelId": "H001",
    "eventId": "EVT-001",
    "eventType": "Checkin",
    "providers": ["FAKE"],
    "data": { "guestName": "Jane Doe" }
  }'
```

Phản hồi mong đợi:

```json
HTTP 202 Accepted
X-Correlation-Id: <guid>

{ "status": "accepted", "correlationId": "<guid>" }
```

---

## Chạy Host

### Chạy dưới dạng Console / HTTP API (mặc định)

```bash
dotnet run --project src/PmsIntegration.Host/PmsIntegration.Host.csproj \
  --environment Production
```

Tiến trình chạy Kestrel và khởi động `ProviderConsumerOrchestrator` như một hosted background service. Cả API và các consumer đều chạy trong cùng một tiến trình.

### Chạy dưới dạng Windows Service

1. Publish dưới dạng self-contained executable:

```powershell
dotnet publish src\PmsIntegration.Host\PmsIntegration.Host.csproj `
  -c Release -r win-x64 --self-contained true `
  -o C:\services\pms-integration
```

2. Cài đặt bằng `sc.exe`:

```powershell
sc.exe create PmsIntegrationService `
  binPath= "C:\services\pms-integration\PmsIntegration.Host.exe" `
  start= auto
sc.exe start PmsIntegrationService
```

> **Lưu ý:** Để bật Windows Service host, `Program.cs` phải gọi `builder.Host.UseWindowsService()` trước `builder.Build()`. Hãy kiểm tra điều này trước khi triển khai dưới dạng service; thêm vào nếu dự án chưa có.

3. Log được ghi vào Elasticsearch index được cấu hình trong `appsettings.json`. Windows Event Log **không** được sử dụng theo mặc định.

### Chọn môi trường

Truyền tên môi trường qua biến môi trường chuẩn `ASPNETCORE_ENVIRONMENT` hoặc cờ CLI `--environment`. `appsettings.{Environment}.json` được merge lên trên `appsettings.json`.

---

## Cấu hình tối thiểu

Tất cả các khóa nằm trong `appsettings.json`. Chỉ các khóa bên dưới là bắt buộc để khởi động dịch vụ.  
Mọi thứ còn lại đều có giá trị mặc định hợp lý.

```json
{
  "PmsSecurity": {
    "FixedToken": "BẮT BUỘC — đặt thành secret mạnh trong production",
    "HeaderName": "X-PMS-TOKEN"
  },

  "RabbitMq": {
    "Host": "localhost",
    "Port": 5672,
    "VirtualHost": "/",
    "UserName": "guest",
    "Password": "guest"
  },

  "Queues": {
    "RetryDelaySeconds": 30,
    "MaxRetryAttempts": 3,
    "ProviderQueues": {
      "FAKE":  "q.pms.fake",
      "TIGER": "q.pms.tiger",
      "OPERA": "q.pms.opera"
    }
  },

  "Providers": {
    "TIGER": {
      "BaseUrl": "https://api.tiger-pms.example.com",
      "ApiKey": "",
      "ApiSecret": "",
      "TimeoutSeconds": 15
    },
    "OPERA": {
      "BaseUrl": "https://api.opera-pms.example.com",
      "ClientId": "",
      "ClientSecret": "",
      "TimeoutSeconds": 20
    },
    "FAKE": {
      "BaseUrl": "https://fake.provider.local",
      "ApiKey": "fake-api-key",
      "TimeoutSeconds": 10,
      "SimulateFailure": false,
      "SimulatedStatusCode": 503
    }
  }
}
```

> **Bảo mật:** Không bao giờ commit thông tin đăng nhập thật. Sử dụng biến môi trường hoặc secrets manager trong production.  
> `PmsSecurity:FixedToken` phải được thay đổi theo từng môi trường.

---

## Cấu trúc dự án

```
PmsIntegration.sln
src/
  PmsIntegration.Host/          ← Điểm vào ASP.NET + background consumers
  PmsIntegration.Application/   ← Use-cases, định tuyến sự kiện, phân loại retry
  PmsIntegration.Core/          ← Hợp đồng, interfaces, domain model (không có deps)
  PmsIntegration.Infrastructure/← RabbitMQ, Serilog/Elastic, idempotency, clock
  Providers/
    PmsIntegration.Providers.Abstractions/  ← Helper PmsProviderBase
    PmsIntegration.Providers.Fake/          ← Nhà cung cấp Fake (testing/dev)
    PmsIntegration.Providers.Tiger/         ← Nhà cung cấp Tiger PMS
    PmsIntegration.Providers.Opera/         ← Nhà cung cấp Opera PMS
tests/
  PmsIntegration.Infrastructure.Tests/
  PmsIntegration.Providers.Fake.Tests/
```

---

## Đọc thêm

| Tài liệu | Mục đích |
|---|---|
| [ARCHITECTURE.md](ARCHITECTURE.md) | Ranh giới tầng, luồng phụ thuộc, vòng đời job, nơi đặt code mới |
| [PROVIDERS.md](PROVIDERS.md) | Thêm nhà cung cấp mới, template, quy tắc DI, kiểm thử |
| [CONVENTIONS.md](CONVENTIONS.md) | Quy tắc đặt tên, trường log, đặt tên hàng đợi, tham chiếu bị cấm |
| [TROUBLESHOOTING.md](TROUBLESHOOTING.md) | Các lỗi thường gặp và cách chẩn đoán |
