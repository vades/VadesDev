// Path: VadesDev.Rag.Tests/Documents/DocumentChunkerTests.cs
using VadesDev.Rag.Documents;
using VadesDev.Rag.Models;

namespace VadesDev.Rag.Tests.Documents;

/// <summary>
/// Tests for DocumentChunker: paragraph splitting, heading joining, ids and line endings.
/// </summary>
public class DocumentChunkerTests
{
    /// <summary>
    /// Chunks a text as the document "test.md", so each test only has to write the text.
    /// </summary>
    private static DocumentChunk[] Chunk(string text)
    {
        return DocumentChunker.Chunk(new SourceDocument("test.md", text));
    }

    /// <summary>
    /// A one-line heading is joined with the paragraph below it, and its # marker stays
    /// (the sanitizer runs after the chunker).
    /// </summary>
    [Fact]
    public void Chunk_JoinsHeadingWithFollowingParagraph()
    {
        var chunk = Assert.Single(Chunk("# Support\n\nHours are 9 to 5."));

        Assert.Equal("# Support\nHours are 9 to 5.", chunk.Text);
    }

    /// <summary>
    /// Without headings, each paragraph becomes one chunk.
    /// </summary>
    [Fact]
    public void Chunk_CreatesOneChunkPerParagraph()
    {
        var chunks = Chunk("First paragraph.\n\nSecond paragraph.");

        Assert.Equal(["First paragraph.", "Second paragraph."], chunks.Select(c => c.Text).ToArray());
    }

    /// <summary>
    /// A heading at the end of the text has no paragraph below it and stays a chunk of its own.
    /// </summary>
    [Fact]
    public void Chunk_KeepsLastHeadingAlone()
    {
        var chunks = Chunk("# Intro\n\nSome text.\n\n# Last");

        Assert.Equal(["# Intro\nSome text.", "# Last"], chunks.Select(c => c.Text).ToArray());
    }

    /// <summary>
    /// A paragraph that starts with # but has several lines is not a heading, so nothing is joined to it.
    /// </summary>
    [Fact]
    public void Chunk_DoesNotTreatMultiLineParagraphAsHeading()
    {
        var chunks = Chunk("# Title\nsecond line\n\nNext paragraph.");

        Assert.Equal(["# Title\nsecond line", "Next paragraph."], chunks.Select(c => c.Text).ToArray());
    }

    /// <summary>
    /// Ids start at 1 and look like "test.md-1". The chunk's Source is the document's source.
    /// </summary>
    [Fact]
    public void Chunk_NumbersIdsFromOneWithSourceName()
    {
        var chunks = Chunk("A\n\nB\n\nC");

        Assert.Equal(["test.md-1", "test.md-2", "test.md-3"], chunks.Select(c => c.Id).ToArray());
        Assert.All(chunks, c => Assert.Equal("test.md", c.Source));
    }

    /// <summary>
    /// Paragraphs are split correctly when the text uses Windows line endings (\r\n).
    /// </summary>
    [Fact]
    public void Chunk_SplitsAtWindowsLineEndings()
    {
        var chunks = Chunk("First\r\n\r\nSecond\r\n\r\nThird");

        Assert.Equal(["First", "Second", "Third"], chunks.Select(c => c.Text).ToArray());
    }

    /// <summary>
    /// A blank line that contains spaces or tabs still separates two paragraphs.
    /// </summary>
    [Fact]
    public void Chunk_SplitsAtBlankLineWithSpaces()
    {
        var chunks = Chunk("First\n  \t \nSecond");

        Assert.Equal(["First", "Second"], chunks.Select(c => c.Text).ToArray());
    }

    /// <summary>
    /// Several blank lines in a row do not create empty chunks.
    /// </summary>
    [Fact]
    public void Chunk_IgnoresExtraBlankLines()
    {
        var chunks = Chunk("First\n\n\n\n\nSecond");

        Assert.Equal(["First", "Second"], chunks.Select(c => c.Text).ToArray());
    }

    /// <summary>
    /// An empty text or a text with only whitespace gives no chunks.
    /// </summary>
    [Fact]
    public void Chunk_ReturnsNoChunksForEmptyOrWhitespaceText()
    {
        Assert.Empty(Chunk(""));
        Assert.Empty(Chunk("   \r\n\r\n \t  "));
    }
}