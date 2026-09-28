using System.Text.RegularExpressions;

namespace DevStudio.Core.Extensions;

/// <summary>A deterministic <c>Major.Minor.Patch</c> version (SKILL.md §9 [Phase 10]) — no
/// pre-release/build-metadata suffixes this phase, kept intentionally minimal.</summary>
public readonly struct ExtensionVersion : IEquatable<ExtensionVersion>, IComparable<ExtensionVersion>
{
    private static readonly Regex Pattern = new(@"^(\d+)\.(\d+)\.(\d+)$", RegexOptions.Compiled);

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    public ExtensionVersion(int major, int minor, int patch)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    public static bool TryParse(string? value, out ExtensionVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var match = Pattern.Match(value);
        if (!match.Success) return false;

        version = new ExtensionVersion(
            int.Parse(match.Groups[1].Value),
            int.Parse(match.Groups[2].Value),
            int.Parse(match.Groups[3].Value));
        return true;
    }

    public int CompareTo(ExtensionVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0) return major;
        var minor = Minor.CompareTo(other.Minor);
        if (minor != 0) return minor;
        return Patch.CompareTo(other.Patch);
    }

    public bool Equals(ExtensionVersion other) => Major == other.Major && Minor == other.Minor && Patch == other.Patch;
    public override bool Equals(object? obj) => obj is ExtensionVersion other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch);
    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    public static bool operator ==(ExtensionVersion left, ExtensionVersion right) => left.Equals(right);
    public static bool operator !=(ExtensionVersion left, ExtensionVersion right) => !left.Equals(right);
    public static bool operator <(ExtensionVersion left, ExtensionVersion right) => left.CompareTo(right) < 0;
    public static bool operator <=(ExtensionVersion left, ExtensionVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >(ExtensionVersion left, ExtensionVersion right) => left.CompareTo(right) > 0;
    public static bool operator >=(ExtensionVersion left, ExtensionVersion right) => left.CompareTo(right) >= 0;
}

/// <summary>DevStudio's own extension-API version — distinct from any build/git version, and the
/// only version an <see cref="ExtensionManifest.HostVersionRange"/> is ever checked against
/// (SKILL.md §9, §34 [Phase 10]).</summary>
public static class ExtensionHostInfo
{
    public static readonly ExtensionVersion HostVersion = new(1, 0, 0);
}

/// <summary>A minimal, space-separated range of comparator clauses (e.g. <c>">=1.0.0 &lt;2.0.0"</c>),
/// ANDed together — enough to express "at least this version" and "before this version" without a
/// full semver-range grammar (SKILL.md §9 [Phase 10]).</summary>
public sealed class ExtensionVersionRange
{
    private readonly IReadOnlyList<(string Operator, ExtensionVersion Bound)> _clauses;
    private readonly string _raw;

    private ExtensionVersionRange(string raw, IReadOnlyList<(string, ExtensionVersion)> clauses)
    {
        _raw = raw;
        _clauses = clauses;
    }

    public static bool TryParse(string? value, out ExtensionVersionRange? range)
    {
        range = null;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var clauses = new List<(string, ExtensionVersion)>();
        foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var op = token switch
            {
                _ when token.StartsWith(">=", StringComparison.Ordinal) => ">=",
                _ when token.StartsWith("<=", StringComparison.Ordinal) => "<=",
                _ when token.StartsWith('>') => ">",
                _ when token.StartsWith('<') => "<",
                _ when token.StartsWith('=') => "=",
                _ => null,
            };
            if (op is null) return false;

            var versionText = token[op.Length..];
            if (!ExtensionVersion.TryParse(versionText, out var bound)) return false;

            clauses.Add((op, bound));
        }

        if (clauses.Count == 0) return false;

        range = new ExtensionVersionRange(value, clauses);
        return true;
    }

    public bool IsSatisfiedBy(ExtensionVersion version) => _clauses.All(clause => clause.Operator switch
    {
        ">=" => version >= clause.Bound,
        "<=" => version <= clause.Bound,
        ">" => version > clause.Bound,
        "<" => version < clause.Bound,
        "=" => version == clause.Bound,
        _ => false,
    });

    public override string ToString() => _raw;
}
