# Providers

> How to understand, extend, and test provider modules in the PMS Integration Service.

---

## Table of Contents

1. [What a Provider Is](#what-a-provider-is)
2. [Existing Providers](#existing-providers)
3. [Add a New Provider in 30 Minutes](#add-a-new-provider-in-30-minutes)
4. [Provider Template](#provider-template)
5. [DI Rules](#di-rules)
6. [Testing per Provider](#testing-per-provider)

---

## What a Provider Is

A **provider** is a self-contained .NET project that knows how to:

1. Map an `IntegrationJob` into a provider-specific `ProviderRequest` (`BuildRequestAsync` — pure mapping, no I/O)
2. Send that request to the external provider API and return a `ProviderResponse` (`SendAsync` — HTTP transport only)

The rest of the pipeline (queue publishing, retry, idempotency, audit logging) is handled by `Infrastructure` and `Application`. The provider module never touches a queue.

**Core contract:**

```csharp
public interface IPmsProvider
{
    string ProviderKey { get; }  // e.g. "TIGER"
    Task<ProviderRequest> BuildRequestAsync(IntegrationJob job, CancellationToken ct);
    Task<ProviderResponse> SendAsync(ProviderRequest request, CancellationToken ct);
}
```

Providers may extend `PmsProviderBase` (from `PmsIntegration.Providers.Abstractions`) or implement `IPmsProvider` directly.

---

## Existing Providers

| Provider key | Project | Config section | Notes |
|---|---|---|---|
| `FAKE` | `PmsIntegration.Providers.Fake` | `Providers:FAKE` | Dev/test only. Can simulate failures via `SimulateFailure` + `SimulatedStatusCode` |
| `TIGER` | `PmsIntegration.Providers.Tiger` | `Providers:TIGER` | Auth via `ApiKey` + `ApiSecret` |
| `OPERA` | `PmsIntegration.Providers.Opera` | `Providers:OPERA` | Auth via `ClientId` + `ClientSecret` |

### Fake provider extras

`FakeOptions` exposes two test helpers:

| Key | Type | Default | Effect |
|---|---|---|---|
| `SimulateFailure` | bool | `false` | Forces `FakeClient` to return a failing response |
| `SimulatedStatusCode` | int | `503` | HTTP status code returned when `SimulateFailure = true` |

Use these in development to exercise the retry and DLQ paths without needing a real provider.

---

## Add a New Provider in 30 Minutes

Follow this checklist exactly. No existing file in `Core`, `Application`, `Infrastructure`, or `Host` needs to be changed except where noted.

### Checklist

- [ ] **1. Create the project**

  ```
  src/PmsIntegration.Providers.Acme/
  ```

  Add a `ProjectReference` to `PmsIntegration.Providers.Abstractions`.  
  Add a `ProjectReference` to `PmsIntegration.Core`.

- [ ] **2. Create the options class**

  ```csharp
  // AcmeOptions.cs
  namespace PmsIntegration.Providers.Acme;

  public sealed class AcmeOptions
  {
      public string BaseUrl { get; set; } = string.Empty;
      public string ApiKey  { get; set; } = string.Empty;
      public int    TimeoutSeconds { get; set; } = 15;
      // Add provider-specific auth fields here
  }
  ```

- [ ] **3. Create the mapper**

  ```
  Mapping/AcmeMapper.cs
  ```

  Pure function, no I/O. Converts `IntegrationJob.Data` to the provider-specific schema.

- [ ] **4. Create the request builder**

  ```
  AcmeRequestBuilder.cs
  ```

  Calls the mapper; returns a populated `ProviderRequest`. No network calls.

- [ ] **5. Create the HTTP client**

  ```
  AcmeClient.cs
  ```

  Resolves the named `HttpClient` (`"ACME"`), sends `ProviderRequest.Body`, returns `ProviderResponse`.

- [ ] **6. Create the provider class**

  Extend `PmsProviderBase` or implement `IPmsProvider` directly.  
  Set `ProviderKey => "ACME"` (uppercase, matches the config key).

- [ ] **7. Create the DI extension method**

  ```
  DI/AcmeServiceExtensions.cs
  ```

  See the [template](#di-extension-template) below.

- [ ] **8. Add the two config entries**

  In `appsettings.json`:
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

- [ ] **9. Register in Host (two lines total)**

  In `src/PmsIntegration.Host/PmsIntegration.Host.csproj` add:
  ```xml
  <ProjectReference Include="..\PmsIntegration.Providers.Acme\PmsIntegration.Providers.Acme.csproj" />
  ```

  In `src/PmsIntegration.Host/Providers/ProvidersServiceExtensions.cs` add:
  ```csharp
  services.AddAcmeProvider(configuration);
  ```
  This must be called **before** `services.AddSingleton<IPmsProviderFactory, PmsProviderFactory>()`.

- [ ] **10. Build and verify**

  ```bash
  dotnet build PmsIntegration.sln
  ```

  The `ProviderConsumerOrchestrator` discovers all `IPmsProvider` registrations automatically. No changes to `Host/Background/` are needed.

- [ ] **11. Write tests** — see [Testing per Provider](#testing-per-provider).

---

## Provider Template

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

/// <summary>Pure mapping — no I/O, fully unit-testable.</summary>
public sealed class AcmeMapper
{
    public AcmeJobPayload Map(IntegrationJob job)
    {
        // TODO: map job.Data fields to the Acme API schema
        return new AcmeJobPayload
        {
            HotelId   = job.HotelId,
            EventType = job.EventType,
            // ...
        };
    }
}

// TODO: define AcmeJobPayload to match the Acme API request body
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
            // TODO: set the correct endpoint path
            Endpoint    = "/api/events",
            Body        = JsonSerializer.Serialize(payload),
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

        using var content = new StringContent(request.Body, Encoding.UTF8, "application/json");
        // TODO: add provider-specific auth header, e.g. client.DefaultRequestHeaders.Add("X-Api-Key", _options.ApiKey)
        var response = await client.PostAsync(request.Endpoint, content, ct);

        return new ProviderResponse
        {
            StatusCode  = (int)response.StatusCode,
            IsSuccess   = response.IsSuccessStatusCode,
            RawBody     = await response.Content.ReadAsStringAsync(ct)
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

### DI extension template

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PmsIntegration.Core.Abstractions;
using PmsIntegration.Providers.Acme.Mapping;

namespace PmsIntegration.Providers.Acme.DI;

public static class AcmeServiceExtensions
{
    /// <summary>
    /// Registers the Acme provider.
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

## DI Rules

1. **Config binding** — always bind to `Providers:<PROVIDER_KEY>` using `services.Configure<XxxOptions>(configuration.GetSection("Providers:ACME"))`.

2. **Named HttpClient** — always use the provider key as the named client identifier (e.g. `"ACME"`). Configure `BaseAddress` and `Timeout` inside the factory action; do not capture `IOptions<T>` at construction time outside of DI.

3. **Lifetimes** — mappers, request builders, and clients are `Singleton`. This is safe because they are stateless.

4. **IPmsProvider registration** — must be `services.AddSingleton<IPmsProvider, AcmeProvider>()`. This is how `PmsProviderFactory` discovers all providers via `IEnumerable<IPmsProvider>`.

5. **Registration order** — all `AddXxxProvider()` calls in `ProvidersServiceExtensions` must come **before** `services.AddSingleton<IPmsProviderFactory, PmsProviderFactory>()`.

6. **No direct instantiation in Host** — `Program.cs` must never manually instantiate a provider class. It calls `services.AddProviders(configuration)` only.

7. **No cross-provider references** — `PmsIntegration.Providers.Acme` must not reference `PmsIntegration.Providers.Tiger` or any other `Providers.*` project.

---

## Testing per Provider

Each provider project should have a corresponding test project: `PmsIntegration.Providers.<Name>.Tests`.

### Unit — Mapper tests

Test the mapper in complete isolation. No mocks needed.

```csharp
[Fact]
public void Maps_checkin_event_to_acme_payload()
{
    var mapper = new AcmeMapper();
    var job = new IntegrationJob
    {
        HotelId   = "H001",
        EventType = "Checkin",
        // populate Data as needed
    };

    var result = mapper.Map(job);

    Assert.Equal("H001", result.HotelId);
    Assert.Equal("Checkin", result.EventType);
}
```

### Unit — Request builder tests

Verify the `ProviderRequest` shape without touching HTTP.

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

### Unit — RetryClassifier tests

`RetryClassifier` lives in `Application`, not in the provider, but each provider's error scenarios should be covered:

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

### Integration — Provider integration tests (optional)

Use the `FAKE` provider as a stand-in when writing integration tests that exercise the full pipeline:

```csharp
// FakeProviderIntegrationTests.cs already exists at:
// tests/PmsIntegration.Providers.Fake.Tests/FakeProviderIntegrationTests.cs
// Use it as a reference for writing Acme integration tests.
```

For new providers, an integration test should:
1. Build the DI container with only the target provider registered.
2. Call `IPmsProvider.BuildRequestAsync` with a known `IntegrationJob`.
3. Assert the `ProviderRequest` fields are correct.
4. Optionally call `SendAsync` against a local mock HTTP server (e.g. `WireMock.Net`).

### Test project reference

```xml
<!-- PmsIntegration.Providers.Acme.Tests.csproj -->
<ItemGroup>
  <ProjectReference Include="..\..\src\PmsIntegration.Providers.Acme\PmsIntegration.Providers.Acme.csproj" />
</ItemGroup>
```
