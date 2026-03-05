# ADR-002: PmsProviderFactory in Infrastructure, Resolved via IEnumerable\<IPmsProvider\>

| Field | Value |
|---|---|
| Status | Accepted |
| Date | 2026-03-04 |
| Deciders | TODO: record names |
| Supersedes | — |

---

## Context

The `IPmsProviderFactory` interface is defined in `PmsIntegration.Core`:

```csharp
public interface IPmsProviderFactory
{
    IPmsProvider Get(string providerCode);
    IReadOnlyList<string> RegisteredKeys { get; }
    IReadOnlyCollection<string> GetRegisteredProviderCodes();
}
```

`ProcessIntegrationJobHandler` (in `Application`) calls `IPmsProviderFactory.Get(providerCode)`
to resolve the correct `IPmsProvider` without any switch/case.

Two questions needed answers:

1. **Where does the factory implementation live?** — `Core`, `Application`, `Infrastructure`, or `Host`?
2. **How does the factory discover all registered providers?** — compile-time list, reflection, or DI injection?

---

## Decision

### 1. The implementation `PmsProviderFactory` lives in `PmsIntegration.Infrastructure`

`Infrastructure` already holds the implementations of all `Core` interfaces
(`IQueuePublisher`, `IAuditLogger`, `IIdempotencyStore`, etc.).
The factory is an implementation detail, not a business rule, so it belongs in `Infrastructure`.

This keeps `Core` free of concrete types and `Application` free of infrastructure knowledge.

Dependency flow is preserved:

```
Application  →  Core (IPmsProviderFactory interface)
Infrastructure  →  Core (implements IPmsProviderFactory)
                →  Providers.Abstractions (receives IEnumerable<IPmsProvider>)
Host  →  Infrastructure (registers PmsProviderFactory)
```

### 2. Provider discovery uses `IEnumerable<IPmsProvider>` injected by the DI container

The factory receives all registered `IPmsProvider` instances from the DI container at
construction time and indexes them into a case-insensitive dictionary keyed by `ProviderKey`.

```csharp
// Infrastructure/Providers/PmsProviderFactory.cs  (simplified)
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
            throw new InvalidOperationException($"No provider registered for key '{key}'.");
        return provider;
    }

    public IReadOnlyList<string> RegisteredKeys =>
        _providers.Keys.ToList().AsReadOnly();

    public IReadOnlyCollection<string> GetRegisteredProviderCodes() =>
        _providers.Keys;
}
```

Registration in `ProvidersServiceExtensions` (Host):

```csharp
services.AddFakeProvider(configuration);
services.AddTigerProvider(configuration);
services.AddOperaProvider(configuration);
// MUST come after all AddXxxProvider() calls:
services.AddSingleton<IPmsProviderFactory, PmsProviderFactory>();
```

---

## Alternatives Considered

### A — Factory in Core

Place the dictionary-building logic directly in `Core` so that `Application` does not depend
on `Infrastructure` at all.

**Rejected because:**
- `Core` must have zero concrete implementations and zero DI/container knowledge.
- Accepting `IEnumerable<IPmsProvider>` in a constructor implies the class is DI-managed,
  which is an infrastructure concern.

### B — Factory in Application

Put `PmsProviderFactory` in `Application` layer next to the use-cases that consume it.

**Rejected because:**
- `Application` must not contain infrastructure implementations.
- It would also create a hidden dependency on IEnumerable resolution (a DI mechanism)
  inside the business layer.

### C — Factory in Host

Register a lambda or manual dictionary in `Program.cs`.

**Rejected because:**
- `Host` is the composition root; business/infrastructure logic does not belong there.
- Would require changes to `Host` every time a provider is added, and would mix wiring
  concern with discovery logic.

### D — Manual switch/case in ProcessIntegrationJobHandler

Embed provider resolution directly in the handler.

**Rejected because:**
- Explicitly forbidden by the plugin architecture — see ADR-001.
- Forces `Application` to reference all `Providers.*` projects.

### E — Reflection / assembly scan

Discover `IPmsProvider` implementations at runtime via reflection.

**Rejected because:**
- No compile-time safety.
- Adds startup latency and complexity.
- All providers are known at compile time; dynamic discovery adds no value.

---

## Consequences

### Positive

- Zero changes to `Application` or `Core` when a new provider is added.
  The DI container automatically includes the new `IPmsProvider` in the `IEnumerable<IPmsProvider>`
  passed to `PmsProviderFactory`.
- `ProcessIntegrationJobHandler` calls `factory.Get(providerCode)` — one line, no branching.
- `RegisteredKeys` enables health-check endpoints and startup logging without extra coupling.
- `PmsProviderFactory` is fully unit-testable: construct it with a list of test doubles.

### Negative / Trade-offs

- **Registration order matters.** `PmsProviderFactory` must be registered after all
  `AddXxxProvider()` calls in `ProvidersServiceExtensions`. If a provider is added after the
  factory registration, it will be silently absent from the dictionary.
  Mitigation: `ProvidersServiceExtensions` documents this constraint in its XML summary,
  and startup should log `RegisteredKeys` so the omission is immediately visible.
- Duplicate `ProviderKey` values across two providers cause an exception at construction time
  (the `ToDictionary` call throws). This is a fail-fast behaviour — desirable.
- `Infrastructure` gains a reference to `Providers.Abstractions` exclusively to receive
  `IEnumerable<IPmsProvider>`. This is the only cross-cutting reference in the dependency graph.

---

## How to Implement

1. Ensure `PmsIntegration.Infrastructure.csproj` has a `ProjectReference` to
   `PmsIntegration.Providers.Abstractions`.
2. Create `Infrastructure/Providers/PmsProviderFactory.cs` implementing `IPmsProviderFactory`.
3. In `InfrastructureServiceExtensions.AddInfrastructure()`, do **not** register
   `PmsProviderFactory` — registration belongs in `ProvidersServiceExtensions` in Host
   so it runs after all providers are wired.
4. In `ProvidersServiceExtensions.AddProviders()`, call all `AddXxxProvider()` methods first,
   then `services.AddSingleton<IPmsProviderFactory, PmsProviderFactory>()` last.

---

## How to Validate

1. **Build:** `dotnet build PmsIntegration.sln` — no errors.
2. **Duplicate key guard:** register two providers with the same `ProviderKey`; the process must throw at startup, not silently return the wrong provider.
3. **Unknown key guard:** call `factory.Get("NONEXISTENT")` in a test; verify `InvalidOperationException` is thrown with the key name in the message.
4. **RegisteredKeys:** at startup log `factory.RegisteredKeys` and confirm all expected provider codes are present.
5. **No switch/case:** `grep -rn "switch\|if.*providerCode\|if.*ProviderKey" src/PmsIntegration.Application/` must return no provider-dispatch logic.
6. **Unit test:** construct `PmsProviderFactory` with a `List<IPmsProvider>` of fakes; call `Get` and assert the correct instance is returned.
