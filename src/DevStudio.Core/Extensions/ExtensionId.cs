using System.Text.RegularExpressions;

namespace DevStudio.Core.Extensions;

/// <summary>
/// A stable extension identity (SKILL.md §8 [Phase 10]) — never a filesystem path. Format is a
/// dot-separated, lowercase, hyphen-permitting namespace (e.g. <c>devstudio.sample.extension</c>
/// or the simpler <c>publisher.name</c>): at least two segments, each starting with a letter or
/// digit and containing only lowercase letters, digits, and hyphens. Case policy: lowercase only
/// — <c>TryParse</c> never lowercases input for you, so a manifest with any uppercase character
/// in its <c>id</c> is rejected rather than silently normalized. Maximum length
/// <see cref="MaxLength"/> characters.
/// </summary>
public readonly struct ExtensionId : IEquatable<ExtensionId>
{
    public const int MaxLength = 128;

    private static readonly Regex Pattern = new(@"^[a-z0-9][a-z0-9-]*(\.[a-z0-9][a-z0-9-]*)+$", RegexOptions.Compiled);

    public string Value { get; }

    private ExtensionId(string value) => Value = value;

    public static bool TryParse(string? value, out ExtensionId id)
    {
        id = default;
        if (string.IsNullOrEmpty(value)) return false;
        if (value.Length > MaxLength) return false;
        if (!Pattern.IsMatch(value)) return false;

        id = new ExtensionId(value);
        return true;
    }

    public bool Equals(ExtensionId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is ExtensionId other && Equals(other);
    public override int GetHashCode() => Value?.GetHashCode(StringComparison.Ordinal) ?? 0;
    public override string ToString() => Value ?? string.Empty;

    public static bool operator ==(ExtensionId left, ExtensionId right) => left.Equals(right);
    public static bool operator !=(ExtensionId left, ExtensionId right) => !left.Equals(right);
}
