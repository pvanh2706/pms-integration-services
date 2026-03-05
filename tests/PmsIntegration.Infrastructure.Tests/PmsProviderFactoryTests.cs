using FluentAssertions;
using PmsIntegration.Core.Abstractions;
using PmsIntegration.Core.Contracts;
using PmsIntegration.Infrastructure.Providers;
using Xunit;

namespace PmsIntegration.Infrastructure.Tests;

/// <summary>
/// Unit tests for <see cref="PmsProviderFactory"/>.
///
/// Uses lightweight inline stubs — no mocking framework required.
/// No DI container is spun up; all tests construct the factory directly.
/// </summary>
public sealed class PmsProviderFactoryTests
{
    // -----------------------------------------------------------------------
    // Stubs
    // -----------------------------------------------------------------------

    /// <summary>
    /// Minimal <see cref="IPmsProvider"/> stub.
    /// Only <see cref="ProviderKey"/> is exercised by the factory;
    /// the other methods throw to surface any unexpected call.
    /// </summary>
    private sealed class StubProvider : IPmsProvider
    {
        public StubProvider(string providerKey) => ProviderKey = providerKey;

        public string ProviderKey { get; }

        public Task<ProviderRequest> BuildRequestAsync(IntegrationJob job, CancellationToken ct = default)
            => throw new NotImplementedException("Not called by factory tests.");

        public Task<ProviderResponse> SendAsync(ProviderRequest request, CancellationToken ct = default)
            => throw new NotImplementedException("Not called by factory tests.");
    }

    // -----------------------------------------------------------------------
    // Constructor validation — null / empty ProviderKey
    // -----------------------------------------------------------------------

    [Fact]
    public void Constructor_WhenProviderKeyIsNull_ThrowsArgumentException()
    {
        // Arrange
        var providers = new[] { new StubProvider(null!) };

        // Act
        var act = () => new PmsProviderFactory(providers);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*null or empty*");
    }

    [Fact]
    public void Constructor_WhenProviderKeyIsEmpty_ThrowsArgumentException()
    {
        // Arrange
        var providers = new[] { new StubProvider(string.Empty) };

        // Act
        var act = () => new PmsProviderFactory(providers);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*null or empty*");
    }

    [Fact]
    public void Constructor_WhenProviderKeyIsWhitespace_ThrowsArgumentException()
    {
        // Arrange
        var providers = new[] { new StubProvider("   ") };

        // Act
        var act = () => new PmsProviderFactory(providers);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*null or empty*");
    }

    // -----------------------------------------------------------------------
    // Constructor validation — duplicate keys
    // -----------------------------------------------------------------------

    [Fact]
    public void Constructor_WhenDuplicateProviderKey_SameCase_ThrowsArgumentException()
    {
        // Arrange
        var providers = new[]
        {
            new StubProvider("TIGER"),
            new StubProvider("TIGER")
        };

        // Act
        var act = () => new PmsProviderFactory(providers);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Duplicate provider key*'TIGER'*");
    }

    [Fact]
    public void Constructor_WhenDuplicateProviderKey_DifferentCase_ThrowsArgumentException()
    {
        // Arrange — "tiger" vs "TIGER" must be treated as the same key
        var providers = new[]
        {
            new StubProvider("tiger"),
            new StubProvider("TIGER")
        };

        // Act
        var act = () => new PmsProviderFactory(providers);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Duplicate provider key*");
    }

    // -----------------------------------------------------------------------
    // Get() — resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Get_WithExactKey_ReturnsCorrectProvider()
    {
        // Arrange
        var tiger = new StubProvider("TIGER");
        var opera = new StubProvider("OPERA");
        var factory = new PmsProviderFactory(new[] { tiger, opera });

        // Act
        var result = factory.Get("TIGER");

        // Assert
        result.Should().BeSameAs(tiger);
    }

    [Fact]
    public void Get_WithLowercaseKey_ResolvesCaseInsensitively()
    {
        // Arrange
        var tiger = new StubProvider("TIGER");
        var factory = new PmsProviderFactory(new[] { tiger });

        // Act
        var result = factory.Get("tiger");

        // Assert
        result.Should().BeSameAs(tiger);
    }

    [Fact]
    public void Get_WithMixedCaseKey_ResolvesCaseInsensitively()
    {
        // Arrange
        var opera = new StubProvider("OPERA");
        var factory = new PmsProviderFactory(new[] { opera });

        // Act
        var result = factory.Get("Opera");

        // Assert
        result.Should().BeSameAs(opera);
    }

    [Fact]
    public void Get_WithUnknownKey_ThrowsInvalidOperationException()
    {
        // Arrange
        var factory = new PmsProviderFactory(new[] { new StubProvider("TIGER") });

        // Act
        var act = () => factory.Get("NONEXISTENT");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'NONEXISTENT'*");
    }

    // -----------------------------------------------------------------------
    // GetRegisteredProviderCodes() / RegisteredKeys
    // -----------------------------------------------------------------------

    [Fact]
    public void GetRegisteredProviderCodes_ReturnsAllRegisteredKeys()
    {
        // Arrange
        var factory = new PmsProviderFactory(new[]
        {
            new StubProvider("FAKE"),
            new StubProvider("TIGER"),
            new StubProvider("OPERA")
        });

        // Act
        var codes = factory.GetRegisteredProviderCodes();

        // Assert
        codes.Should().BeEquivalentTo(new[] { "FAKE", "TIGER", "OPERA" });
    }

    [Fact]
    public void RegisteredKeys_ReturnsAllRegisteredKeys()
    {
        // Arrange
        var factory = new PmsProviderFactory(new[]
        {
            new StubProvider("FAKE"),
            new StubProvider("TIGER")
        });

        // Act + Assert
        factory.RegisteredKeys.Should().BeEquivalentTo(new[] { "FAKE", "TIGER" });
    }

    [Fact]
    public void GetRegisteredProviderCodes_WhenEmpty_ReturnsEmptyCollection()
    {
        // Arrange
        var factory = new PmsProviderFactory(Array.Empty<IPmsProvider>());

        // Act + Assert
        factory.GetRegisteredProviderCodes().Should().BeEmpty();
    }
}
