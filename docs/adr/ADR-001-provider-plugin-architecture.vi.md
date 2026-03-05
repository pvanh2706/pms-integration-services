# ADR-001: Kiến trúc Provider Plugin (Cách tiếp cận B1)

| Trường | Giá trị |
|---|---|
| Trạng thái | Đã chấp nhận |
| Ngày | 2026-03-04 |
| Người quyết định | TODO: ghi lại tên |
| Thay thế | — |

---

## Bối cảnh

Dịch vụ Tích hợp PMS phải chuyển tiếp các sự kiện PMS (ví dụ Checkin, Checkout) đến nhiều Provider API bên thứ ba (Tiger, Opera, và các provider khác có thể được thêm vào sau).

Mỗi provider:
- Có hình dạng HTTP API và cơ chế xác thực khác nhau.
- Có thể được thêm, thay đổi, hoặc xóa độc lập với các provider khác.
- Không được ảnh hưởng đến hành vi của các provider khác khi bị lỗi hoặc thay đổi cấu hình.

Hai cách tiếp cận cấu trúc đã được đánh giá:

| Cách tiếp cận | Mô tả |
|---|---|
| **A — Monolithic switch** | Một lớp `ProviderDispatcher` duy nhất chứa `switch(providerCode)` và import trực tiếp HTTP client của mọi provider. |
| **B1 — Provider Plugin (đã chọn)** | Mỗi provider là một dự án .NET độc lập triển khai interface `IPmsProvider` chung và tự đăng ký qua một DI extension method. Phần còn lại của pipeline là provider-agnostic. |

---

## Quyết định

**Sử dụng Cách tiếp cận B1: Provider Plugin.**

Mỗi provider được đóng gói trong dự án riêng (`PmsIntegration.Providers.<Tên>`).
Dự án triển khai `IPmsProvider` (được định nghĩa trong `PmsIntegration.Core`) và đăng ký
chính nó qua một extension method duy nhất (`services.AddXxxProvider(configuration)`).

Pipeline cốt lõi (`ReceivePmsEventHandler`, `ProcessIntegrationJobHandler`) gọi
`IPmsProviderFactory.Get(providerCode)` — nó không bao giờ chứa `switch` hay chuỗi `if/else`
theo provider code.

Lớp cơ sở tùy chọn `PmsProviderBase` (trong `PmsIntegration.Providers.Abstractions`) cung cấp
một triển khai tiện lợi; các provider cũng có thể triển khai `IPmsProvider` trực tiếp.

### Interface chính

```csharp
// PmsIntegration.Core/Abstractions/IPmsProvider.cs
public interface IPmsProvider
{
    string ProviderKey { get; }  // ví dụ "TIGER" — in hoa, khớp với config key
    Task<ProviderRequest> BuildRequestAsync(IntegrationJob job, CancellationToken ct = default);
    Task<ProviderResponse> SendAsync(ProviderRequest request, CancellationToken ct = default);
}
```

### Cấu trúc nội bộ bắt buộc của mỗi dự án provider

```
PmsIntegration.Providers.<Tên>/
  <Tên>Options.cs               ← config POCO, gắn với Providers:<KEY>
  <Tên>RequestBuilder.cs        ← pure mapping IntegrationJob → ProviderRequest
  <Tên>Client.cs                ← chỉ là HTTP transport
  Mapping/<Tên>Mapper.cs        ← data mapping có thể unit-test
  DI/<Tên>ServiceExtensions.cs  ← services.AddXxxProvider(configuration)
```

### Composition root (chỉ Host)

`Host` tham chiếu các dự án provider **chỉ** để đăng ký DI.
Tất cả lệnh gọi provider trong `Application` đi qua `IPmsProvider` — không bao giờ là kiểu cụ thể.

```csharp
// ProvidersServiceExtensions.cs (Host)
services.AddFakeProvider(configuration);
services.AddTigerProvider(configuration);
services.AddOperaProvider(configuration);
// AddAcmeProvider(...) — một dòng để thêm nhà cung cấp mới
services.AddSingleton<IPmsProviderFactory, PmsProviderFactory>();
```

---

## Các lựa chọn đã xem xét

### A — Monolithic switch/case dispatcher

```csharp
// Pattern bị từ chối
switch (job.ProviderKey)
{
    case "TIGER": await _tigerClient.SendAsync(...); break;
    case "OPERA": await _operaClient.SendAsync(...); break;
}
```

**Bị từ chối vì:**
- Mỗi nhà cung cấp mới đòi hỏi chỉnh sửa code `Application` hoặc `Host` hiện có.
- Tất cả HTTP client của provider phải được tham chiếu từ một lớp duy nhất, tạo ra tight coupling.
- Lỗi compile-time trong một provider chặn việc triển khai tất cả các provider.
- Vi phạm nguyên tắc Open/Closed.

### B2 — Plugin qua reflection / assembly scanning

Tải các assembly provider khi chạy từ đường dẫn thư mục được cấu hình, phát hiện triển khai `IPmsProvider` qua reflection.

**Bị từ chối vì:**
- Thêm tính không ổn định khi chạy mà không có compile-time safety cho provider graph.
- Làm phức tạp pipeline build và triển khai.
- Số lượng provider là nhỏ và đã biết tại compile time; dynamic loading không mang lại lợi ích.

---

## Hệ quả

### Tích cực

- Thêm một provider không chạm vào **bất kỳ file hiện có nào** trong `Core`, `Application`, hoặc `Infrastructure`.
- Mỗi provider có thể được phát triển, kiểm thử và triển khai độc lập.
- Lỗi provider bị cô lập; mapper hoặc client lỗi chỉ ảnh hưởng đến hàng đợi của provider đó.
- Unit-testing một provider không cần mock infrastructure — chỉ cần khởi tạo mapper hoặc request builder.

### Tiêu cực / Đánh đổi

- Dự án provider mới thêm `ProjectReference` vào `Host.csproj` và một lệnh gọi trong `ProvidersServiceExtensions`. Điều này là có chủ ý và tối thiểu nhưng vẫn là thay đổi `Host`.
- Tất cả dự án provider được compile vào một deployment unit duy nhất. Triển khai thực sự độc lập đòi hỏi chiến lược tải plugin phức tạp hơn (Cách tiếp cận B2, đã bị từ chối).
- `PmsProviderFactory` phải được đăng ký **sau** tất cả lệnh gọi `AddXxxProvider()`. Xác nhận thứ tự sai gây lỗi runtime. Xem ADR-002.

---

## Cách triển khai

Xem checklist đầy đủ trong [PROVIDERS.md — Thêm nhà cung cấp mới trong 30 phút](../../PROVIDERS.md#add-a-new-provider-in-30-minutes).

Tóm tắt:
1. Tạo dự án `PmsIntegration.Providers.<Tên>` tham chiếu `Providers.Abstractions` và `Core`.
2. Triển khai năm file bắt buộc (Options, RequestBuilder, Client, Mapper, DI extension).
3. Đặt `ProviderKey` thành chuỗi provider code in hoa.
4. Thêm config entry `Providers:<KEY>` và `Queues:ProviderQueues:<KEY>`.
5. Thêm `ProjectReference` vào `Host.csproj`.
6. Thêm `services.AddXxxProvider(configuration)` trong `ProvidersServiceExtensions` trước `services.AddSingleton<IPmsProviderFactory, PmsProviderFactory>()`.

---

## Cách xác nhận

1. **Build gate:** `dotnet build PmsIntegration.sln` phải thành công không có lỗi.
2. **DI resolution:** Khi khởi động, `IPmsProviderFactory.RegisteredKeys` phải chứa provider key mới.
3. **Không có switch/case:** `grep -r "switch.*providerCode\|switch.*ProviderKey" src/` phải không trả về kết quả nào ngoài `PmsProviderFactory`.
4. **Ranh giới tầng:** `dotnet list src/PmsIntegration.Application/PmsIntegration.Application.csproj reference` không được bao gồm bất kỳ dự án `Providers.*` nào.
5. **Unit test:** Provider mapper test phải pass mà không cần mock infrastructure.
6. **Integration smoke test:** Gửi `POST /api/pms/events` với provider code mới trong mảng `providers`; xác minh `job.success` xuất hiện trong Kibana.
