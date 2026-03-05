# Nhà cung cấp (Providers)

> Cách hiểu, mở rộng và kiểm thử các module provider trong Dịch vụ Tích hợp PMS.

---

## Mục lục

1. [Provider là gì](#provider-là-gì)
2. [Các provider hiện có](#các-provider-hiện-có)
3. [Thêm nhà cung cấp mới trong 30 phút](#thêm-nhà-cung-cấp-mới-trong-30-phút)
4. [Template provider](#template-provider)
5. [Quy tắc DI](#quy-tắc-di)
6. [Kiểm thử theo từng provider](#kiểm-thử-theo-từng-provider)

---

## Provider là gì

Một **provider** là một dự án .NET độc lập biết cách:

1. Map một `IntegrationJob` thành `ProviderRequest` dành riêng cho nhà cung cấp (`BuildRequestAsync` — chỉ là pure mapping, không có I/O)
2. Gửi request đó đến external provider API và trả về `ProviderResponse` (`SendAsync` — chỉ là HTTP transport)

Phần còn lại của pipeline (publish lên hàng đợi, retry, idempotency, audit logging) được xử lý bởi `Infrastructure` và `Application`. Provider module không bao giờ đụng đến hàng đợi.

**Hợp đồng cốt lõi:**

```csharp
public interface IPmsProvider
{
    string ProviderKey { get; }  // ví dụ "TIGER"
    Task<ProviderRequest> BuildRequestAsync(IntegrationJob job, CancellationToken ct);
    Task<ProviderResponse> SendAsync(ProviderRequest request, CancellationToken ct);
}
```

Providers có thể kế thừa `PmsProviderBase` (từ `PmsIntegration.Providers.Abstractions`) hoặc triển khai `IPmsProvider` trực tiếp.

---

## Các provider hiện có

| Provider key | Dự án | Config section | Ghi chú |
|---|---|---|---|
| `FAKE` | `PmsIntegration.Providers.Fake` | `Providers:FAKE` | Chỉ dùng cho dev/test. Có thể giả lập lỗi qua `SimulateFailure` + `SimulatedStatusCode` |
| `TIGER` | `PmsIntegration.Providers.Tiger` | `Providers:TIGER` | Xác thực qua `ApiKey` + `ApiSecret` |
| `OPERA` | `PmsIntegration.Providers.Opera` | `Providers:OPERA` | Xác thực qua `ClientId` + `ClientSecret` |

### Các tính năng thêm của Fake provider

`FakeOptions` cung cấp hai helper kiểm thử:

| Key | Kiểu | Mặc định | Hiệu ứng |
|---|---|---|---|
| `SimulateFailure` | bool | `false` | Bắt `FakeClient` trả về response thất bại |
| `SimulatedStatusCode` | int | `503` | HTTP status code trả về khi `SimulateFailure = true` |

Dùng các tùy chọn này trong development để kiểm tra path retry và DLQ mà không cần nhà cung cấp thật.

---

## Thêm nhà cung cấp mới trong 30 phút

Làm theo checklist này chính xác. Không có file nào trong `Core`, `Application`, `Infrastructure`, hoặc `Host` cần thay đổi ngoại trừ những nơi được chỉ định.

### Checklist

- [ ] **1. Tạo dự án**

  ```
  src/Providers/PmsIntegration.Providers.Acme/
  Thêm `ProjectReference` tới `PmsIntegration.Core`.
  ```

- [ ] **2. Tạo lớp options**

  ```csharp
  // AcmeOptions.cs
  namespace PmsIntegration.Providers.Acme;

  public sealed class AcmeOptions
  {
      public string BaseUrl { get; set; } = string.Empty;
      public string ApiKey  { get; set; } = string.Empty;
      public int    TimeoutSeconds { get; set; } = 15;
      // Thêm các trường xác thực dành riêng cho provider tại đây
  }
  ```

- [ ] **3. Tạo mapper**

  ```
  Mapping/AcmeMapper.cs
  ```

  Pure function, không có I/O. Chuyển đổi `IntegrationJob.Data` thành schema dành riêng cho provider.

- [ ] **4. Tạo request builder**

  ```
  AcmeRequestBuilder.cs
  ```

  Gọi mapper; trả về `ProviderRequest` đã điền đầy đủ. Không có gọi mạng.

- [ ] **5. Tạo HTTP client**

  ```
  AcmeClient.cs
  ```

  Phân giải named `HttpClient` (`"ACME"`), gửi `ProviderRequest.Body`, trả về `ProviderResponse`.

- [ ] **6. Tạo lớp provider**

  Kế thừa `PmsProviderBase` hoặc triển khai `IPmsProvider` trực tiếp.  
  Đặt `ProviderKey => "ACME"` (in hoa, khớp với config key).

- [ ] **7. Tạo DI extension method**

  ```
  DI/AcmeServiceExtensions.cs
  ```

  Xem [template](#template-di-extension) bên dưới.

- [ ] **8. Thêm hai config entry**

  Trong `appsettings.json`:
  ```json
  "Providers": {
    "ACME": {
      "BaseUrl": "https://api.acme-pms.example.com",
      "ApiKey": "",
      "TimeoutSeconds": 15
    }
  },
  "Queues": {
    "ProviderQueues": {
      "ACME": "q.pms.acme"
    }
  }
  ```

- [ ] **9. Đăng ký trong Host (tổng cộng hai dòng)**

  Trong `src/PmsIntegration.Host/PmsIntegration.Host.csproj` thêm:
  ```xml
  <ProjectReference Include="..\Providers\PmsIntegration.Providers.Acme\PmsIntegration.Providers.Acme.csproj" />
  ```

  Trong `src/PmsIntegration.Host/Providers/ProvidersServiceExtensions.cs` thêm:
  ```csharp
  services.AddAcmeProvider(configuration);
  ```
  Phải được gọi **trước** `services.AddSingleton<IPmsProviderFactory, PmsProviderFactory>()`.

- [ ] **10. Build và xác minh**

  ```bash
  dotnet build PmsIntegration.sln
  ```

  `ProviderConsumerOrchestrator` tự động phát hiện tất cả đăng ký `IPmsProvider`. Không cần thay đổi `Host/Background/`.

- [ ] **11. Viết tests** — xem [Kiểm thử theo từng provider](#kiểm-thử-theo-từng-provider).

---

## Template provider

### AcmeOptions.cs

```csharp
namespace PmsIntegration.Providers.Acme;

public sealed class AcmeOptions
{
    public string BaseUrl       { get; set; } = string.Empty;
    public string ApiKey        { get; set; } = string.Empty;
    public int    TimeoutSeconds { get; set; } = 15;
}
```

### Mapping/AcmeMapper.cs

```csharp
using PmsIntegration.Core.Contracts;

namespace PmsIntegration.Providers.Acme.Mapping;

/// <summary>Pure mapping — không có I/O, có thể unit-test hoàn toàn.</summary>
public sealed class AcmeMapper
{
    public AcmeJobPayload Map(IntegrationJob job)
    {
        // TODO: map các trường job.Data vào schema Acme API
        return new AcmeJobPayload
        {
            HotelId   = job.HotelId,
            EventType = job.EventType,
            // ...
        };
    }
}

// TODO: định nghĩa AcmeJobPayload khớp với request body của Acme API
public sealed class AcmeJobPayload
{
    public string HotelId   { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
}
```

### AcmeRequestBuilder.cs

```csharp
using System.Text.Json;
using PmsIntegration.Core.Contracts;
using PmsIntegration.Providers.Acme.Mapping;

namespace PmsIntegration.Providers.Acme;

public sealed class AcmeRequestBuilder
{
    private readonly AcmeMapper _mapper;

    public AcmeRequestBuilder(AcmeMapper mapper) => _mapper = mapper;

    public Task<ProviderRequest> BuildAsync(IntegrationJob job, CancellationToken ct = default)
    {
        var payload = _mapper.Map(job);
        return Task.FromResult(new ProviderRequest
        {
            ProviderKey = "ACME",
            // TODO: đặt endpoint path đúng
            Endpoint    = "/api/events",
            JsonBody    = JsonSerializer.Serialize(payload),
            Headers     = new Dictionary<string, string>
            {
                ["X-Correlation-Id"] = job.CorrelationId ?? string.Empty
            }
        });
    }
}
```

### AcmeClient.cs

```csharp
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Options;
using PmsIntegration.Core.Contracts;

namespace PmsIntegration.Providers.Acme;

public sealed class AcmeClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly AcmeOptions        _options;

    public AcmeClient(IHttpClientFactory httpFactory, IOptions<AcmeOptions> options)
    {
        _httpFactory = httpFactory;
        _options     = options.Value;
    }

    public async Task<ProviderResponse> SendAsync(ProviderRequest request, CancellationToken ct = default)
    {
        var client = _httpFactory.CreateClient("ACME");

        using var content = new StringContent(request.JsonBody ?? string.Empty, Encoding.UTF8, "application/json");
        // TODO: thêm auth header dành riêng cho provider, ví dụ client.DefaultRequestHeaders.Add("X-Api-Key", _options.ApiKey)
        var response = await client.PostAsync(request.Endpoint, content, ct);

        return new ProviderResponse
        {
            StatusCode = (int)response.StatusCode,
            Body       = await response.Content.ReadAsStringAsync(ct)
        };
    }
}
```

### AcmeProvider.cs

```csharp
using PmsIntegration.Core.Contracts;
using PmsIntegration.Providers.Abstractions;

namespace PmsIntegration.Providers.Acme;

public sealed class AcmeProvider : PmsProviderBase
{
    public override string ProviderKey => "ACME";

    private readonly AcmeRequestBuilder _requestBuilder;
    private readonly AcmeClient         _client;

    public AcmeProvider(AcmeRequestBuilder requestBuilder, AcmeClient client)
    {
        _requestBuilder = requestBuilder;
        _client         = client;
    }

    public override Task<ProviderRequest> BuildRequestAsync(IntegrationJob job, CancellationToken ct = default)
        => _requestBuilder.BuildAsync(job, ct);

    public override Task<ProviderResponse> SendAsync(ProviderRequest request, CancellationToken ct = default)
        => _client.SendAsync(request, ct);
}
```

### Template DI extension

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PmsIntegration.Core.Abstractions;
using PmsIntegration.Providers.Acme.Mapping;

namespace PmsIntegration.Providers.Acme.DI;

public static class AcmeServiceExtensions
{
    /// <summary>
    /// Đăng ký Acme provider.
    /// Config section: <c>Providers:ACME</c>
    /// </summary>
    public static IServiceCollection AddAcmeProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AcmeOptions>(configuration.GetSection("Providers:ACME"));

        services.AddHttpClient("ACME", (sp, client) =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AcmeOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrl);
            client.Timeout     = TimeSpan.FromSeconds(opts.TimeoutSeconds);
        });

        services.AddSingleton<AcmeMapper>();
        services.AddSingleton<AcmeRequestBuilder>();
        services.AddSingleton<AcmeClient>();
        services.AddSingleton<IPmsProvider, AcmeProvider>();

        return services;
    }
}
```

---

## Quy tắc DI

1. **Binding config** — luôn bind vào `Providers:<PROVIDER_KEY>` dùng `services.Configure<XxxOptions>(configuration.GetSection("Providers:ACME"))`.

2. **Named HttpClient** — luôn dùng provider key làm định danh named client (ví dụ `"ACME"`). Cấu hình `BaseAddress` và `Timeout` bên trong factory action; không capture `IOptions<T>` tại thời điểm khởi tạo ngoài DI.

3. **Lifetimes** — mapper, request builder và client đều là `Singleton`. Điều này an toàn vì chúng là stateless.

4. **Đăng ký IPmsProvider** — phải dùng `services.AddSingleton<IPmsProvider, AcmeProvider>()`. Đây là cách `PmsProviderFactory` phát hiện tất cả provider qua `IEnumerable<IPmsProvider>`.

5. **Thứ tự đăng ký** — tất cả lệnh gọi `AddXxxProvider()` trong `ProvidersServiceExtensions` phải đến **trước** `services.AddSingleton<IPmsProviderFactory, PmsProviderFactory>()`.

6. **Không khởi tạo trực tiếp trong Host** — `Program.cs` không bao giờ được khởi tạo thủ công một lớp provider. Nó chỉ gọi `services.AddProviders(configuration)`.

7. **Không có tham chiếu cross-provider** — `PmsIntegration.Providers.Acme` không được tham chiếu `PmsIntegration.Providers.Tiger` hoặc bất kỳ dự án `Providers.*` nào khác.

---

## Kiểm thử theo từng provider

Mỗi dự án provider nên có một dự án test tương ứng: `PmsIntegration.Providers.<Tên>.Tests`.

### Unit — Tests cho Mapper

Kiểm thử mapper hoàn toàn độc lập. Không cần mock.

```csharp
[Fact]
public void Maps_checkin_event_to_acme_payload()
{
    var mapper = new AcmeMapper();
    var job = new IntegrationJob
    {
        HotelId   = "H001",
        EventType = "Checkin",
        // điền Data khi cần
    };

    var result = mapper.Map(job);

    Assert.Equal("H001", result.HotelId);
    Assert.Equal("Checkin", result.EventType);
}
```

### Unit — Tests cho Request builder

Xác minh hình dạng `ProviderRequest` mà không động đến HTTP.

```csharp
[Fact]
public async Task BuildAsync_sets_correct_endpoint()
{
    var builder = new AcmeRequestBuilder(new AcmeMapper());
    var job = BuildTestJob();

    var request = await builder.BuildAsync(job);

    Assert.Equal("ACME", request.ProviderKey);
    Assert.Equal("/api/events", request.Endpoint);
}
```

### Unit — Tests cho RetryClassifier

`RetryClassifier` nằm trong `Application`, không phải trong provider, nhưng các tình huống lỗi của từng provider nên được cover:

```csharp
[Theory]
[InlineData(500, IntegrationOutcome.RetryableFailure)]
[InlineData(429, IntegrationOutcome.RetryableFailure)]
[InlineData(400, IntegrationOutcome.NonRetryableFailure)]
public void Classifies_http_status_correctly(int statusCode, IntegrationOutcome expected)
{
    var classifier = new RetryClassifier();
    var outcome    = classifier.Classify(statusCode);
    Assert.Equal(expected, outcome);
}
```

### Integration — Tests tích hợp provider (tùy chọn)

Dùng `FAKE` provider như một stand-in khi viết integration test kiểm tra toàn bộ pipeline:

```csharp
// FakeProviderIntegrationTests.cs đã có sẵn tại:
// tests/PmsIntegration.Providers.Fake.Tests/FakeProviderIntegrationTests.cs
// Dùng làm tài liệu tham khảo khi viết Acme integration tests.
```

Với provider mới, integration test nên:
1. Build DI container chỉ với target provider được đăng ký.
2. Gọi `IPmsProvider.BuildRequestAsync` với một `IntegrationJob` đã biết.
3. Assert các trường `ProviderRequest` là đúng.
4. Tùy chọn gọi `SendAsync` với một mock HTTP server cục bộ (ví dụ `WireMock.Net`).

### Tham chiếu dự án test

```xml
<!-- PmsIntegration.Providers.Acme.Tests.csproj -->
<ItemGroup>
  <ProjectReference Include="..\..\src\Providers\PmsIntegration.Providers.Acme\PmsIntegration.Providers.Acme.csproj" />
</ItemGroup>
```
