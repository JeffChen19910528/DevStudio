using System.Text;

namespace DevStudio.Core.Testing;

/// <summary>
/// A structured test selection (SKILL.md §16 [Phase 8]) — never a raw shell/filter string a
/// caller hands DevStudio. <see cref="ToVsTestFilterExpression"/> is the one place that builds
/// VSTest's real <c>--filter</c> mini-language from this structure; nothing else in DevStudio
/// constructs a filter string, and no caller ever supplies one directly.
/// </summary>
public sealed record TestFilter(IReadOnlyList<string>? FullyQualifiedNames = null, string? TraitName = null, string? TraitValue = null)
{
    /// <summary>Returns null when there is nothing to filter on (run everything).</summary>
    public string? ToVsTestFilterExpression()
    {
        if (FullyQualifiedNames is { Count: > 0 })
        {
            return string.Join("|", FullyQualifiedNames.Select(name => $"FullyQualifiedName={Escape(name)}"));
        }

        if (!string.IsNullOrWhiteSpace(TraitName) && !string.IsNullOrWhiteSpace(TraitValue))
        {
            return $"{Escape(TraitName)}={Escape(TraitValue)}";
        }

        return null;
    }

    /// <summary>VSTest's filter mini-language treats <c>( ) & | ! =</c> and backslash as
    /// operators — escaping them with a leading backslash is the runner's own documented
    /// mechanism for a literal value, not a DevStudio invention.</summary>
    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is '\\' or '(' or ')' or '&' or '|' or '!' or '=' or '~') builder.Append('\\');
            builder.Append(c);
        }
        return builder.ToString();
    }
}
