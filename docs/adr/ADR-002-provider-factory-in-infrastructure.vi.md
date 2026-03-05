# ADR-002: PmsProviderFactory trong Infrastructure, Phân giải qua IEnumerable\<IPmsProvider\>

| Trường | Giá trị |
|---|---|
| Trạng thái | Đã chấp nhận |
| Ngày | 2026-03-04 |
| Người quyết định | TODO: ghi lại tên |
| Thay thế | — |

---

## Bối cảnh

Interface `IPmsProviderFactory` được định nghĩa trong `PmsIntegration.Core`:

```csharp
public interface IPmsProviderFactory
{
    IPmsProvider Get(string providerCode);
    IReadOnlyList<string> RegisteredKeys { get; }
    IReadOnlyCollection<string> GetRegisteredProviderCodes();
}
```

`ProcessIntegrationJobHandler` (trong `Application`) gọi `IPmsProviderFactory.Get(providerCode)`
để phân giải `IPmsProvider` đúng mà không cần switch/case.

Cần trả lời hai câu hỏi:

1. **Triển khai factory sống ở đâu?** — `Core`, `Application`, `Infrastructure`, hay `Host`?
2. **Factory phát hiện tất cả provider đã đăng ký như thế nào?** — danh sách compile-time, reflection, hoặc DI injection?

---

## Quyết định

### 1. Triển khai `PmsProviderFactory` sống trong `PmsIntegration.Infrastructure`

`Infrastructure` đã chứa các triển khai của tất cả interface `Core`
(`IQueuePublisher`, `IAuditLogger`, `IIdempotencyStore`, v.v.).
Factory là một chi tiết triển khai, không phải là quy tắc nghiệp vụ, nên nó thuộc về `Infrastructure`.

Điều này giữ `Core` không có kiểu cụ thể và `Application` không có kiến thức về infrastructure.

Luồng phụ thuộc được bảo toàn:

```
Application  →  Core (IPmsProviderFactory interface)
Infrastructure  →  Core (triển khai IPmsProviderFactory)
                →  Providers.Abstractions (nhận IEnumerable<IPmsProvider>)
Host  →  Infrastructure (đăng ký PmsProviderFactory)
```

### 2. Phát hiện provider sử dụng `IEnumerable<IPmsProvider>` được inject bởi DI container

Factory nhận tất cả `IPmsProvider` instances đã đăng ký từ DI container tại thời điểm
khởi tạo và index chúng vào dictionary case-insensitive theo `ProviderKey`.

```csharp
// Infrastructure/Providers/PmsProviderFactory.cs  (đơn giản hóa)
public sealed class PmsProviderFactory : IPmsProviderFactory
{
    private readonly IReadOnlyDictionary<string, IPmsProvider> _providers;

    public PmsProviderFactory(IEnumerable<IPmsProvider> providers)
    {
        _providers = providers.ToDictionary(
            p => p.ProviderKey.Trim().ToUpperInvariant(),
            p => p,
            StringComparer.OrdinalIgnoreCase);
    }

    public IPmsProvider Get(string providerCode)
    {
        var key = providerCode.Trim().ToUpperInvariant();
        if (!_providers.TryGetValue(key, out var provider))
            throw new InvalidOperationException(
                $"No IPmsProvider registered for provider code '{key}'. " +
                $"Registered codes: [{string.Join(", ", RegisteredKeys)}]");
        return provider;
    }

    public IReadOnlyList<string> RegisteredKeys =>
        _providers.Keys.ToList().AsReadOnly();

    public IReadOnlyCollection<string> GetRegisteredProviderCodes() =>
        _providers.Keys;
}
```

Đăng ký trong `ProvidersServiceExtensions` (Host):

```csharp
services.AddFakeProvider(configuration);
services.AddTigerProvider(configuration);
services.AddOperaProvider(configuration);
// PHẢI đến sau tất cả lệnh gọi AddXxxProvider():
services.AddSingleton<IPmsProviderFactory, PmsProviderFactory>();
```

---

## Các lựa chọn đã xem xét

### A — Factory trong Core

Đặt logic xây dựng dictionary trực tiếp trong `Core` để `Application` không phụ thuộc vào `Infrastructure`.

**Bị từ chối vì:**
- `Core` phải không có triển khai concrete hoặc DI/container knowledge.
- Nhận `IEnumerable<IPmsProvider>` trong constructor ngụ ý class được quản lý bởi DI, đây là mối quan tâm infrastructure.

### B — Factory trong Application

Đặt `PmsProviderFactory` trong tầng `Application` bên cạnh các use-case sử dụng nó.

**Bị từ chối vì:**
- `Application` không được chứa implementations infrastructure.
- Cũng tạo ra hidden dependency vào IEnumerable resolution (cơ chế DI) bên trong tầng business.

### C — Factory trong Host

Đăng ký lambda hoặc dictionary thủ công trong `Program.cs`.

**Bị từ chối vì:**
- `Host` là composition root; business/infrastructure logic không thuộc về đó.
- Đòi hỏi thay đổi `Host` mỗi khi provider được thêm, và trộn lẫn wiring concern với discovery logic.

### D — Manual switch/case trong ProcessIntegrationJobHandler

Nhúng provider resolution trực tiếp vào handler.

**Bị từ chối vì:**
- Bị cấm rõ ràng bởi kiến trúc plugin — xem ADR-001.
- Buộc `Application` phải tham chiếu tất cả dự án `Providers.*`.

### E — Reflection / assembly scan

Phát hiện triển khai `IPmsProvider` khi chạy qua reflection.

**Bị từ chối vì:**
- Không có compile-time safety.
- Thêm startup latency và độ phức tạp.
- Tất cả provider đã biết tại compile time; dynamic discovery không mang thêm giá trị.

---

## Hệ quả

### Tích cực

- Không có thay đổi nào cho `Application` hoặc `Core` khi thêm provider mới.
  DI container tự động đưa `IPmsProvider` mới vào `IEnumerable<IPmsProvider>` được truyền vào `PmsProviderFactory`.
- `ProcessIntegrationJobHandler` gọi `factory.Get(providerCode)` — một dòng, không có phân nhánh.
- `RegisteredKeys` cho phép health-check endpoint và startup logging mà không cần coupling thêm.
- `PmsProviderFactory` có thể unit-test hoàn toàn: khởi tạo với list test double.

### Tiêu cực / Đánh đổi

- **Thứ tự đăng ký quan trọng.** `PmsProviderFactory` phải được đăng ký sau tất cả lệnh gọi `AddXxxProvider()` trong `ProvidersServiceExtensions`. Nếu provider được thêm sau khi đăng ký factory, nó sẽ vắng mặt một cách im lặng khỏi dictionary. Giảm thiểu: `ProvidersServiceExtensions` ghi lại ràng buộc này trong XML summary, và startup nên log `RegisteredKeys` để bỏ sót được thấy ngay lập tức.
- Giá trị `ProviderKey` trùng lặp trên hai provider gây exception tại thời điểm khởi tạo (lệnh gọi `ToDictionary` throw). Đây là hành vi fail-fast — mong muốn.
- `Infrastructure` có thêm tham chiếu đến `Providers.Abstractions` chỉ để nhận `IEnumerable<IPmsProvider>`. Đây là tham chiếu cross-cutting duy nhất trong dependency graph.

---

## Cách triển khai

1. Đảm bảo `PmsIntegration.Infrastructure.csproj` có `ProjectReference` đến `PmsIntegration.Providers.Abstractions`.
2. Tạo `Infrastructure/Providers/PmsProviderFactory.cs` triển khai `IPmsProviderFactory`.
3. Trong `InfrastructureServiceExtensions.AddInfrastructure()`, **không** đăng ký `PmsProviderFactory` — việc đăng ký thuộc về `ProvidersServiceExtensions` trong Host để chạy sau tất cả provider được wired.
4. Trong `ProvidersServiceExtensions.AddProviders()`, gọi tất cả phương thức `AddXxxProvider()` trước, rồi `services.AddSingleton<IPmsProviderFactory, PmsProviderFactory>()` cuối cùng.

---

## Cách xác nhận

1. **Build:** `dotnet build PmsIntegration.sln` — không có lỗi.
2. **Kiểm tra duplicate key:** đăng ký hai provider với cùng `ProviderKey`; tiến trình phải throw khi khởi động, không trả về provider sai một cách im lặng.
3. **Kiểm tra unknown key:** gọi `factory.Get("NONEXISTENT")` trong test; xác minh `InvalidOperationException` được throw với tên key trong message.
4. **RegisteredKeys:** khi khởi động log `factory.RegisteredKeys` và xác nhận tất cả provider code mong đợi đều có mặt.
5. **Không có switch/case:** `grep -rn "switch\|if.*providerCode\|if.*ProviderKey" src/PmsIntegration.Application/` phải không trả về logic provider-dispatch.
6. **Unit test:** khởi tạo `PmsProviderFactory` với `List<IPmsProvider>` gồm các fake; gọi `Get` và assert instance đúng được trả về.
