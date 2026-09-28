using DevStudio.Infrastructure.Terminal;
using Xunit;

namespace DevStudio.Infrastructure.Tests;

public class ShellLocatorTests
{
    [Fact]
    public void GetDefaultShellExecutable_returns_a_shell_that_actually_exists_on_this_machine()
    {
        var shell = ShellLocator.GetDefaultShellExecutable();

        Assert.False(string.IsNullOrWhiteSpace(shell));

        // cmd.exe/sh are resolved by name rather than full path in the last-resort fallback,
        // so only assert existence when a full path was returned.
        if (Path.IsPathRooted(shell))
        {
            Assert.True(File.Exists(shell), $"Resolved shell path does not exist: {shell}");
        }
    }
}
