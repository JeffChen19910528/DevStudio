using DevStudio.Core.Editor;
using DevStudio.UI.ViewModels;
using Xunit;

namespace DevStudio.UI.Tests;

public class DocumentViewModelTests
{
    [Fact]
    public void IsModified_becomes_true_when_text_diverges_from_loaded_content()
    {
        var document = new DocumentViewModel("C:/Program.cs", "Program.cs", "original", TextEncodingKind.Utf8, LineEndingKind.Lf);

        Assert.False(document.IsModified);

        document.Text = "changed";

        Assert.True(document.IsModified);
    }

    [Fact]
    public void MarkSaved_clears_modified_state_against_the_new_baseline()
    {
        var document = new DocumentViewModel(null, "Untitled", "", TextEncodingKind.Utf8, LineEndingKind.Lf);
        document.Text = "new content";

        document.MarkSaved();

        Assert.False(document.IsModified);

        document.Text = "new content"; // setting the same value again must stay clean
        Assert.False(document.IsModified);
    }

    [Fact]
    public void ReloadFromDisk_replaces_text_and_clears_modified_and_pending_external_change()
    {
        var document = new DocumentViewModel("C:/a.txt", "a.txt", "old", TextEncodingKind.Utf8, LineEndingKind.Lf)
        {
            Text = "edited locally",
            HasExternalChangePending = true
        };

        document.ReloadFromDisk("from disk");

        Assert.Equal("from disk", document.Text);
        Assert.False(document.IsModified);
        Assert.False(document.HasExternalChangePending);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(5, 1, 6)]
    [InlineData(6, 2, 1)]
    [InlineData(12, 3, 1)]
    public void CaretIndex_changes_recompute_line_and_column(int caretIndex, int expectedLine, int expectedColumn)
    {
        var document = new DocumentViewModel(null, "Untitled", "line1\nline2\nline3", TextEncodingKind.Utf8, LineEndingKind.Lf)
        {
            CaretIndex = caretIndex
        };

        Assert.Equal(expectedLine, document.Line);
        Assert.Equal(expectedColumn, document.Column);
    }
}
