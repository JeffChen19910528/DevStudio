using DevStudio.Core.Errors;
using Xunit;

namespace DevStudio.Core.Tests;

public class DevStudioExceptionTests
{
    [Fact]
    public void Carries_error_kind_and_context_for_actionable_error_display()
    {
        var context = new Dictionary<string, string>
        {
            ["Project"] = "MyApp",
            ["Toolchain"] = ".NET 10",
            ["ExitCode"] = "1",
        };

        var exception = new DevStudioException(DevStudioErrorKind.BuildError, "Build failed.", context);

        Assert.Equal(DevStudioErrorKind.BuildError, exception.Kind);
        Assert.Equal("MyApp", exception.Context["Project"]);
        Assert.Equal("Build failed.", exception.Message);
    }

    [Fact]
    public void Context_defaults_to_empty_when_not_supplied()
    {
        var exception = new DevStudioException(DevStudioErrorKind.InternalError, "Unexpected failure.");

        Assert.Empty(exception.Context);
    }
}
