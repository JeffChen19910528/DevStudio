using DevStudio.Core.Workspace;
using Xunit;

namespace DevStudio.Core.Tests;

public class BuildConfigurationTests
{
    [Fact]
    public void Debug_preset_keeps_symbols_and_skips_optimization()
    {
        var config = BuildConfiguration.Debug;

        Assert.True(config.IncludeDebugSymbols);
        Assert.False(config.Optimize);
        Assert.Equal("Debug", config.Name);
    }

    [Fact]
    public void Release_preset_optimizes_and_drops_symbols()
    {
        var config = BuildConfiguration.Release;

        Assert.False(config.IncludeDebugSymbols);
        Assert.True(config.Optimize);
        Assert.Equal("Release", config.Name);
    }
}
