using VadesDev.Rag.Documents;

namespace VadesDev.Rag.Tests.Documents;

/// <summary>
/// Tests for TextSanitizer: one test per cleaning rule, plus line endings and empty input.
/// </summary>
public class TextSanitizerTests
{
    /// <summary>
    /// A # marker at the start of a line is removed, the heading words stay.
    /// </summary>
    [Fact]
    public void Sanitize_RemovesHeadingMarker()
    {
        var result = TextSanitizer.Sanitize("# Support\nCustomer hours");

        Assert.Equal("Support\nCustomer hours", result);
    }

    /// <summary>
    /// A # inside a line (for example "issue #5") stays.
    /// </summary>
    [Fact]
    public void Sanitize_KeepsHashInsideLine()
    {
        var result = TextSanitizer.Sanitize("See issue #5.");

        Assert.Equal("See issue #5.", result);
    }

    /// <summary>
    /// Bold markers (** and __) and backticks are removed, the words stay.
    /// </summary>
    [Fact]
    public void Sanitize_RemovesEmphasisMarkers()
    {
        var result = TextSanitizer.Sanitize("Call **us** at `9 AM` and __now__");

        Assert.Equal("Call us at 9 AM and now", result);
    }

    /// <summary>
    /// A Markdown link keeps only its text.
    /// </summary>
    [Fact]
    public void Sanitize_ReplacesMarkdownLinkWithItsText()
    {
        var result = TextSanitizer.Sanitize("See [our help page](https://example.com/help) for more.");

        Assert.Equal("See our help page for more.", result);
    }

    /// <summary>
    /// A raw URL is removed and the spaces around it collapse.
    /// </summary>
    [Fact]
    public void Sanitize_RemovesRawUrl()
    {
        var result = TextSanitizer.Sanitize("Visit https://example.com/help today");

        Assert.Equal("Visit today", result);
    }

    /// <summary>
    /// Control characters (including NUL, which ChunkHasher uses as separator) and zero-width characters are removed.
    /// </summary>
    [Fact]
    public void Sanitize_RemovesControlAndZeroWidthCharacters()
    {
        var result = TextSanitizer.Sanitize("A\0B\u200BC\u0007D\uFEFFE");

        Assert.Equal("ABCDE", result);
    }

    /// <summary>
    /// Repeated spaces and tabs become one space, and each line is trimmed.
    /// </summary>
    [Fact]
    public void Sanitize_CollapsesWhitespaceAndTrimsLines()
    {
        var result = TextSanitizer.Sanitize("  Hello   world \t again  \n   Second\tline  ");

        Assert.Equal("Hello world again\nSecond line", result);
    }

    /// <summary>
    /// Windows line endings (\r\n) become \n.
    /// </summary>
    [Fact]
    public void Sanitize_ConvertsWindowsLineEndings()
    {
        var result = TextSanitizer.Sanitize("First\r\n\r\nSecond");

        Assert.Equal("First\n\nSecond", result);
    }

    /// <summary>
    /// An empty text stays empty.
    /// </summary>
    [Fact]
    public void Sanitize_ReturnsEmptyTextForEmptyInput()
    {
        Assert.Equal("", TextSanitizer.Sanitize(""));
    }
}