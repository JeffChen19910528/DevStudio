using DevStudio.Core.Workspace;
using Xunit;

namespace DevStudio.Core.Tests;

public class WorkspaceExclusionRulesTests
{
    [Fact]
    public void Default_excludes_common_noise_directories()
    {
        // Regression test: Default is itself a static field whose constructor falls back to
        // DefaultExcludedDirectoryNames. Declaration order matters for .NET static field
        // initialization, and a prior ordering caused Default to be built before that fallback
        // was populated, silently producing an empty rule set.
        Assert.True(WorkspaceExclusionRules.Default.IsExcluded(".git"));
        Assert.True(WorkspaceExclusionRules.Default.IsExcluded("node_modules"));
        Assert.False(WorkspaceExclusionRules.Default.IsExcluded("src"));
    }

    [Fact]
    public void Custom_rules_override_the_default_set()
    {
        var rules = new WorkspaceExclusionRules(new[] { "vendor" });

        Assert.True(rules.IsExcluded("vendor"));
        Assert.False(rules.IsExcluded(".git"));
    }
}
