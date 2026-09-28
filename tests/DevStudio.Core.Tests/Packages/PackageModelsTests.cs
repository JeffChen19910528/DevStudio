using DevStudio.Core.Packages;
using Xunit;

namespace DevStudio.Core.Tests.Packages;

public class PackageModelsTests
{
    [Fact]
    public void PackageManagerCapabilities_None_has_every_capability_false()
    {
        var none = PackageManagerCapabilities.None;

        Assert.False(none.ListInstalled);
        Assert.False(none.Search);
        Assert.False(none.Add);
        Assert.False(none.Remove);
        Assert.False(none.Update);
        Assert.False(none.Restore);
        Assert.False(none.ListOutdated);
        Assert.False(none.ManageSources);
        Assert.False(none.LockfileSupport);
        Assert.False(none.TransitiveDependencySupport);
        Assert.False(none.PrereleaseSupport);
    }

    [Fact]
    public void PackageVersion_IsStable_is_the_inverse_of_IsPrerelease()
    {
        var stable = new PackageVersion("1.0.0", IsPrerelease: false);
        var prerelease = new PackageVersion("1.0.0-beta", IsPrerelease: true);

        Assert.True(stable.IsStable);
        Assert.False(prerelease.IsStable);
    }

    [Fact]
    public void PackageOperationResult_Cancelled_reports_no_success_and_WasCancelled_true()
    {
        var result = PackageOperationResult.Cancelled(PackageOperation.Add, "/proj/a.csproj", "pkg");

        Assert.False(result.Success);
        Assert.True(result.WasCancelled);
        Assert.Equal(PackageOperation.Add, result.Operation);
        Assert.Equal("pkg", result.PackageId);
    }

    [Fact]
    public void PackageOperationResult_Unavailable_reports_no_success_and_a_reason_but_not_cancelled()
    {
        var result = PackageOperationResult.Unavailable(PackageOperation.Restore, "/proj/a.csproj", "not supported for this project");

        Assert.False(result.Success);
        Assert.False(result.WasCancelled);
        Assert.Equal("not supported for this project", result.FailureReason);
    }

    [Fact]
    public void WellKnownPackageManagerIds_P0_ids_are_stable_strings()
    {
        Assert.Equal("nuget", WellKnownPackageManagerIds.NuGet);
        Assert.Equal("pip", WellKnownPackageManagerIds.Pip);
        Assert.Equal("npm", WellKnownPackageManagerIds.Npm);
    }
}
