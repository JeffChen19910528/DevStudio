using DevStudio.Core.Testing;
using Xunit;

namespace DevStudio.Core.Tests;

public class TestFilterTests
{
    [Fact]
    public void No_criteria_produces_no_filter_expression()
    {
        var filter = new TestFilter();

        Assert.Null(filter.ToVsTestFilterExpression());
    }

    [Fact]
    public void A_single_fully_qualified_name_produces_an_exact_match_expression()
    {
        var filter = new TestFilter(FullyQualifiedNames: new[] { "MyApp.Tests.CalculatorTests.Add_ReturnsExpectedValue" });

        Assert.Equal("FullyQualifiedName=MyApp.Tests.CalculatorTests.Add_ReturnsExpectedValue", filter.ToVsTestFilterExpression());
    }

    [Fact]
    public void Multiple_fully_qualified_names_are_combined_with_a_real_VSTest_or_operator()
    {
        var filter = new TestFilter(FullyQualifiedNames: new[] { "A.Test1", "B.Test2" });

        Assert.Equal("FullyQualifiedName=A.Test1|FullyQualifiedName=B.Test2", filter.ToVsTestFilterExpression());
    }

    [Fact]
    public void A_trait_filter_produces_a_name_value_expression()
    {
        var filter = new TestFilter(TraitName: "Category", TraitValue: "Smoke");

        Assert.Equal("Category=Smoke", filter.ToVsTestFilterExpression());
    }

    [Theory]
    [InlineData("A(1)", "A\\(1\\)")]
    [InlineData("A&B", "A\\&B")]
    [InlineData("A|B", "A\\|B")]
    public void Special_VSTest_filter_characters_in_a_name_are_escaped_never_passed_through_raw(string rawName, string escapedName)
    {
        var filter = new TestFilter(FullyQualifiedNames: new[] { rawName });

        Assert.Equal($"FullyQualifiedName={escapedName}", filter.ToVsTestFilterExpression());
    }
}
