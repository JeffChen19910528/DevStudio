namespace DevStudio.UI.Services;

/// <summary>Pure text-search logic behind Find/Replace/Go To Line (SKILL.md §10 [editor]), kept
/// free of any Avalonia dependency so it is trivially unit-testable.</summary>
public static class TextSearchService
{
    /// <summary>Finds the next occurrence of <paramref name="query"/> at or after <paramref name="fromIndex"/>,
    /// wrapping around to the start of the text if nothing is found after it.</summary>
    public static int FindNext(string text, string query, int fromIndex, bool matchCase)
    {
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(text)) return -1;

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var start = Math.Clamp(fromIndex, 0, text.Length);

        var index = text.IndexOf(query, start, comparison);
        if (index >= 0) return index;

        return text.IndexOf(query, 0, comparison);
    }

    public static string ReplaceAll(string text, string query, string replacement, bool matchCase)
    {
        if (string.IsNullOrEmpty(query)) return text;

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var result = new System.Text.StringBuilder();
        var position = 0;

        while (true)
        {
            var index = text.IndexOf(query, position, comparison);
            if (index < 0)
            {
                result.Append(text.AsSpan(position));
                break;
            }

            result.Append(text.AsSpan(position, index - position));
            result.Append(replacement);
            position = index + query.Length;
        }

        return result.ToString();
    }

    /// <summary>Returns the character index of the start of <paramref name="lineNumber"/> (1-based).</summary>
    public static int GetIndexForLine(string text, int lineNumber)
    {
        if (lineNumber <= 1) return 0;

        var currentLine = 1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                currentLine++;
                if (currentLine == lineNumber) return i + 1;
            }
        }

        return text.Length;
    }

    /// <summary>The inverse of <see cref="GetIndexForLine"/>: 1-based line/column for a
    /// character index (SKILL.md §26, §30 [Phase 7] — converted to LSP's 0-based
    /// <c>Position</c> at the language-service boundary, never inside this pure helper).</summary>
    public static (int Line, int Column) GetLineAndColumnForIndex(string text, int index)
    {
        var clamped = Math.Clamp(index, 0, text.Length);
        var line = 1;
        var lineStart = 0;

        for (var i = 0; i < clamped; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        return (line, clamped - lineStart + 1);
    }
}
