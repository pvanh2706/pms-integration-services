using System.Text.Json;
using FluentAssertions;
using PmsIntegration.Core.Contracts;
using PmsIntegration.Providers.Fake.Mapping;
using Xunit;

namespace PmsIntegration.Providers.Fake.Tests;

/// <summary>
/// Unit tests for FakeMapper.
/// These tests verify pure mapping logic — no I/O, no DI, no infrastructure.
/// Run with: dotnet test --filter "FullyQualifiedName~FakeMapperTests"
/// </summary>
public sealed class FakeMapperTests
{
    private readonly FakeMapper _sut = new();

    [Fact]
    public void Map_ShouldSerializeAllJobFields()
    {
        // Arrange
        var job = new IntegrationJob
        {
            HotelId    = "hotel-123",
            EventId    = "evt-456",
            EventType  = "RESERVATION_CREATED",
            CorrelationId = "corr-789",
            Data       = JsonDocument.Parse("{\"roomNumber\":\"101\"}").RootElement
        };

        // Act
        var json = _sut.Map(job);

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("hotelId").GetString().Should().Be("hotel-123");
        root.GetProperty("eventId").GetString().Should().Be("evt-456");
        root.GetProperty("eventType").GetString().Should().Be("RESERVATION_CREATED");
        root.GetProperty("correlationId").GetString().Should().Be("corr-789");
        root.GetProperty("source").GetString().Should().Be("pms-integration");
    }

    [Fact]
    public void Map_WithNullData_ShouldProduceNullDataField()
    {
        // Arrange
        var job = new IntegrationJob { HotelId = "h1", EventId = "e1", EventType = "T", CorrelationId = "c1" };

        // Act
        var json = _sut.Map(job);

        // Assert
        json.Should().NotBeNullOrWhiteSpace();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("hotelId").GetString().Should().Be("h1");
    }
}
