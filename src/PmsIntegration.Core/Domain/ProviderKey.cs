namespace PmsIntegration.Core.Domain;

/// <summary>
/// Strongly-typed, normalized provider key (uppercase, trimmed).
/// </summary>
public sealed class ProviderKey
{
    public string Value { get; }

    public ProviderKey(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("ProviderKey must not be empty.", nameof(raw));

        Value = raw.Trim().ToUpperInvariant();
    }

    public override string ToString() => Value;

    public override bool Equals(object? obj) =>
        obj is ProviderKey other && Value == other.Value;

    public override int GetHashCode() => Value.GetHashCode();

    public static implicit operator string(ProviderKey key) => key.Value;
}
