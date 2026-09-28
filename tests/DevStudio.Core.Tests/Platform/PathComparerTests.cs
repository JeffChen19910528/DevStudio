using DevStudio.Core.Platform;
using Xunit;

namespace DevStudio.Core.Tests.Platform;

/// <summary>SKILL.md §10, §31 [Phase 11]: verifies <see cref="PathComparer"/> reflects the real
/// runtime platform it's actually executing on (via <see cref="OperatingSystem"/>), not a
/// hard-coded assumption — the comparer's identity is asserted against
/// <see cref="OperatingSystem.IsWindows"/>/<see cref="OperatingSystem.IsMacOS"/> themselves,
/// so this test is meaningful (and was actually exercised) on every platform it runs on,
/// including the real Linux run this phase performed.</summary>
public class PathComparerTests
{
    [Fact]
    public void IsCaseInsensitive_matches_the_real_running_platform()
    {
        var expected = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
        Assert.Equal(expected, PathComparer.IsCaseInsensitive);
    }

    [Fact]
    public void Equals_reflects_this_platforms_real_case_sensitivity()
    {
        var result = PathComparer.Equals("Foo.cs", "foo.cs");
        Assert.Equal(PathComparer.IsCaseInsensitive, result);
    }

    [Fact]
    public void Comparer_and_Comparison_agree_with_each_other()
    {
        var viaComparer = PathComparer.Comparer.Equals("Foo.cs", "foo.cs");
        var viaComparison = string.Equals("Foo.cs", "foo.cs", PathComparer.Comparison);
        Assert.Equal(viaComparer, viaComparison);
    }

    [Fact]
    public void Different_paths_are_never_equal_regardless_of_platform()
    {
        Assert.False(PathComparer.Equals("a.cs", "b.cs"));
    }
}
