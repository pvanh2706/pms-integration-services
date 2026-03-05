# ADR-001: Provider Plugin Architecture (Approach B1)

| Field | Value |
|---|---|
| Status | Accepted |
| Date | 2026-03-04 |
| Deciders | TODO: record names |
| Supersedes | — |

---

## Context

The PMS Integration Service must forward PMS events (e.g. Checkin, Checkout) to multiple
third-party Provider APIs (Tiger, Opera, and others that may be added later).

Each provider:
- Has a different HTTP API shape and authentication mechanism.
- May be added, changed, or removed independently of the others.
- Must not affect the behaviour of other providers when it fails or is reconfigured.

Two structural approaches were evaluated:

| Approach | Description |
|---|---|
| **A — Monolithic switch** | A single `ProviderDispatcher` class contains a `switch(providerCode)` and imports every provider's HTTP client directly. |
| **B1 — Provider Plugin (selected)** | Each provider is an isolated .NET project that implements a shared `IPmsProvider` interface and self-registers via a DI extension method. The rest of the pipeline is provider-agnostic. |

---

## Decision

**Use Approach B1: Provider Plugin.**

Every provider is encapsulated in its own project (`PmsIntegration.Providers.<Name>`).
The project implements `IPmsProvider` (defined in `PmsIntegration.Core`) and registers itself
via a single extension method (`services.AddXxxProvider(configuration)`).

The core pipeline (`ReceivePmsEventHandler`, `ProcessIntegrationJobHandler`) calls
`IPmsProviderFactory.Get(providerCode)` — it never contains a `switch` or `if/else` chain
over provider codes.

The optional base class `PmsProviderBase` (in `PmsIntegration.Providers.Abstractions`) provides
a convenience implementation; providers may also implement `IPmsProvider` directly.

### Key interface

```csharp
// PmsIntegration.Core/Abstractions/IPmsProvider.cs
public interface IPmsProvider
{
    string ProviderKey { get; }  // e.g. "TIGER" — uppercase, matches config key
    Task<ProviderRequest> BuildRequestAsync(IntegrationJob job, CancellationToken ct = default);
    Task<ProviderResponse> SendAsync(ProviderRequest request, CancellationToken ct = default);
}
```

### Mandatory internal structure per provider project

```
PmsIntegration.Providers.<Name>/
  <Name>Options.cs               ← config POCO, bound to Providers:<KEY>
  <Name>RequestBuilder.cs        ← pure mapping IntegrationJob → ProviderRequest
  <Name>Client.cs                ← HTTP transport only
  Mapping/<Name>Mapper.cs        ← unit-testable data mapping
  DI/<Name>ServiceExtensions.cs  ← services.AddXxxProvider(configuration)
```

### Composition root (Host only)

`Host` references provider projects **only** for DI registration.
All provider calls in `Application` go through `IPmsProvider` — never a concrete type.

```csharp
// ProvidersServiceExtensions.cs (Host)
services.AddFakeProvider(configuration);
services.AddTigerProvider(configuration);
services.AddOperaProvider(configuration);
// AddAcmeProvider(...) — one line to add a new provider
services.AddSingleton<IPmsProviderFactory, PmsProviderFactory>();
```

---

## Alternatives Considered

### A — Monolithic switch/case dispatcher

```csharp
// Rejected pattern
switch (job.ProviderKey)
{
    case "TIGER": await _tigerClient.SendAsync(...); break;
    case "OPERA": await _operaClient.SendAsync(...); break;
}
```

**Rejected because:**
- Every new provider requires editing existing `Application` or `Host` code.
- All provider HTTP clients must be referenced from a single class, creating tight coupling.
- A compile-time error in one provider blocks deployment of all providers.
- Violates the Open/Closed Principle.

### B2 — Plugin via reflection / assembly scanning

Load provider assemblies at runtime from a configured folder path, discover `IPmsProvider`
implementations via reflection.

**Rejected because:**
- Adds runtime fragility with no compile-time safety for the provider graph.
- Complicates the build and deployment pipeline.
- The number of providers is small and known at compile time; dynamic loading adds no benefit.

---

## Consequences

### Positive

- Adding a provider touches **zero existing files** in `Core`, `Application`, or `Infrastructure`.
- Each provider can be developed, tested, and deployed independently.
- Provider failures are isolated; a bad mapper or client affects only that provider's queue.
- Unit-testing a provider requires no mocking of infrastructure — just instantiate the mapper or request builder.

### Negative / Trade-offs

- A new provider project adds a `ProjectReference` to `Host.csproj` and one call in
  `ProvidersServiceExtensions`. This is intentional and minimal, but it is a change to `Host`.
- All provider projects are compiled into a single deployment unit. Truly independent deployment
  requires a more complex plug-in loading strategy (Approach B2, rejected above).
- The `PmsProviderFactory` must be registered **after** all `AddXxxProvider()` calls.
  Getting this order wrong produces a runtime error. See ADR-002.

---

## How to Implement

See the full checklist in [PROVIDERS.md — Add a new provider in 30 minutes](../../PROVIDERS.md#add-a-new-provider-in-30-minutes).

Summary:
1. Create `PmsIntegration.Providers.<Name>` project referencing `Providers.Abstractions` and `Core`.
2. Implement the mandatory five files (Options, RequestBuilder, Client, Mapper, DI extension).
3. Set `ProviderKey` to the uppercase provider code string.
4. Add `Providers:<KEY>` and `Queues:ProviderQueues:<KEY>` config entries.
5. Add `ProjectReference` to `Host.csproj`.
6. Add `services.AddXxxProvider(configuration)` in `ProvidersServiceExtensions` before
   `services.AddSingleton<IPmsProviderFactory, PmsProviderFactory>()`.

---

## How to Validate

1. **Build gate:** `dotnet build PmsIntegration.sln` must succeed with zero errors.
2. **DI resolution:** At startup `IPmsProviderFactory.RegisteredKeys` must contain the new provider key.
3. **No switch/case:** `grep -r "switch.*providerCode\|switch.*ProviderKey" src/` must return no results outside `PmsProviderFactory`.
4. **Layer boundary:** `dotnet list src/PmsIntegration.Application/PmsIntegration.Application.csproj reference` must not include any `Providers.*` project.
5. **Unit test:** The provider mapper test must pass without any infrastructure mock.
6. **Integration smoke test:** Send a `POST /api/pms/events` with the new provider code in the `providers` array; verify `job.success` appears in Kibana.
