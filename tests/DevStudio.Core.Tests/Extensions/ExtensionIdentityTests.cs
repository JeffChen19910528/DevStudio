using DevStudio.Core.Extensions;
using Xunit;

namespace DevStudio.Core.Tests.Extensions;

public class ExtensionIdTests
{
    [Theory]
    [InlineData("devstudio.sample")]
    [InlineData("devstudio.sample.extension")]
    [InlineData("my-publisher.my-extension-name")]
    [InlineData("a1.b2")]
    public void Valid_ids_are_accepted(string value)
    {
        Assert.True(ExtensionId.TryParse(value, out var id));
        Assert.Equal(value, id.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("noDotAtAll")]
    [InlineData("Devstudio.Sample")]
    [InlineData("devstudio..sample")]
    [InlineData(".devstudio.sample")]
    [InlineData("devstudio.sample.")]
    [InlineData("devstudio sample.extension")]
    [InlineData("devstudio.sample/extension")]
    public void Invalid_ids_are_rejected(string? value)
    {
        Assert.False(ExtensionId.TryParse(value, out _));
    }

    [Fact]
    public void An_id_longer_than_the_maximum_length_is_rejected()
    {
        var tooLong = "publisher." + new string('a', ExtensionId.MaxLength);
        Assert.False(ExtensionId.TryParse(tooLong, out _));
    }

    [Fact]
    public void Equal_ids_compare_equal()
    {
        ExtensionId.TryParse("devstudio.sample", out var a);
        ExtensionId.TryParse("devstudio.sample", out var b);
        Assert.Equal(a, b);
        Assert.True(a == b);
    }
}

public class ExtensionVersionTests
{
    [Theory]
    [InlineData("1.0.0", 1, 0, 0)]
    [InlineData("10.20.30", 10, 20, 30)]
    [InlineData("0.0.1", 0, 0, 1)]
    public void Valid_versions_parse(string text, int major, int minor, int patch)
    {
        Assert.True(ExtensionVersion.TryParse(text, out var version));
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.0")]
    [InlineData("1.0.0.0")]
    [InlineData("1.0.0-beta")]
    [InlineData("v1.0.0")]
    [InlineData("a.b.c")]
    public void Invalid_versions_are_rejected(string? text)
    {
        Assert.False(ExtensionVersion.TryParse(text, out _));
    }

    [Fact]
    public void Versions_compare_by_major_then_minor_then_patch()
    {
        ExtensionVersion.TryParse("1.2.3", out var a);
        ExtensionVersion.TryParse("1.2.4", out var b);
        ExtensionVersion.TryParse("2.0.0", out var c);
        Assert.True(a < b);
        Assert.True(b < c);
        Assert.True(c > a);
    }
}

public class ExtensionVersionRangeTests
{
    [Theory]
    [InlineData(">=1.0.0", "1.0.0", true)]
    [InlineData(">=1.0.0", "0.9.9", false)]
    [InlineData(">=1.0.0 <2.0.0", "1.5.0", true)]
    [InlineData(">=1.0.0 <2.0.0", "2.0.0", false)]
    [InlineData("<2.0.0", "1.9.9", true)]
    public void Range_satisfaction_is_evaluated_correctly(string rangeText, string versionText, bool expected)
    {
        Assert.True(ExtensionVersionRange.TryParse(rangeText, out var range));
        Assert.True(ExtensionVersion.TryParse(versionText, out var version));
        Assert.Equal(expected, range!.IsSatisfiedBy(version));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-range")]
    [InlineData(">=notaversion")]
    [InlineData("~1.0.0")]
    public void Malformed_ranges_are_rejected(string? text)
    {
        Assert.False(ExtensionVersionRange.TryParse(text, out _));
    }
}
