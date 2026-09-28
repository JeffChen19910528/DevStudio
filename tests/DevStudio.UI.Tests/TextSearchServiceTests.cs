using DevStudio.UI.Services;
using Xunit;

namespace DevStudio.UI.Tests;

public class TextSearchServiceTests
{
    [Fact]
    public void FindNext_finds_first_match_when_starting_before_it()
    {
        var index = TextSearchService.FindNext("hello world", "world", fromIndex: 0, matchCase: true);
        Assert.Equal(6, index);
    }

    [Fact]
    public void FindNext_wraps_around_to_the_start_when_nothing_remains_after_fromIndex()
    {
        var text = "needle ... needle";
        var index = TextSearchService.FindNext(text, "needle", fromIndex: 10, matchCase: true);
        Assert.Equal(11, index);

        var wrapped = TextSearchService.FindNext(text, "needle", fromIndex: 12, matchCase: true);
        Assert.Equal(0, wrapped);
    }

    [Fact]
    public void FindNext_is_case_insensitive_unless_matchCase_is_set()
    {
        Assert.Equal(0, TextSearchService.FindNext("Hello", "hello", 0, matchCase: false));
        Assert.Equal(-1, TextSearchService.FindNext("Hello", "hello", 0, matchCase: true));
    }

    [Fact]
    public void ReplaceAll_replaces_every_occurrence()
    {
        var result = TextSearchService.ReplaceAll("foo bar foo baz foo", "foo", "X", matchCase: true);
        Assert.Equal("X bar X baz X", result);
    }

    [Fact]
    public void ReplaceAll_returns_original_text_when_query_is_empty()
    {
        Assert.Equal("unchanged", TextSearchService.ReplaceAll("unchanged", "", "X", matchCase: true));
    }

    [Fact]
    public void GetIndexForLine_returns_start_of_requested_line()
    {
        var text = "line1\nline2\nline3";

        Assert.Equal(0, TextSearchService.GetIndexForLine(text, 1));
        Assert.Equal(6, TextSearchService.GetIndexForLine(text, 2));
        Assert.Equal(12, TextSearchService.GetIndexForLine(text, 3));
    }

    [Fact]
    public void GetIndexForLine_clamps_to_end_of_text_for_out_of_range_line()
    {
        var text = "only one line";
        Assert.Equal(text.Length, TextSearchService.GetIndexForLine(text, 5));
    }
}
