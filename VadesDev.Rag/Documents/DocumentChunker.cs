using System.Text.RegularExpressions;
using VadesDev.Rag.Models;

namespace VadesDev.Rag.Documents;

/// <summary>
/// Splits a document into chunks: one chunk per paragraph, with each heading joined to the paragraph below it.
/// </summary>
public static partial class DocumentChunker
{
    /// <summary>
    /// Returns one chunk per paragraph (headings joined), with ids like "knowledge.md-1".
    /// </summary>
    public static DocumentChunk[] Chunk(SourceDocument document)
    {
        var paragraphs = SplitParagraphs(document.Text);
        var joined = JoinHeadings(paragraphs);

        return [.. joined.Select((text, i) => new DocumentChunk($"{document.Source}-{i + 1}", document.Source, text))];
    }

    /// <summary>
    /// Splits a text into paragraphs at blank lines. Works for \n and \r\n.
    /// </summary>
    private static string[] SplitParagraphs(string text)
    {
        return BlankLineRegex()
            .Split(text)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToArray();
    }

    /// <summary>
    /// Joins each heading paragraph with the paragraph that follows it.
    /// </summary>
    private static List<string> JoinHeadings(string[] paragraphs)
    {
        var result = new List<string>();

        for (int i = 0; i < paragraphs.Length; i++)
        {
            var isJoinable = IsHeading(paragraphs[i]) && i + 1 < paragraphs.Length;

            result.Add(isJoinable ? $"{paragraphs[i]}\n{paragraphs[i + 1]}" : paragraphs[i]);

            if (isJoinable)
            {
                i++;
            }
        }

        return result;
    }

    /// <summary>
    /// A heading is a paragraph of one line that starts with #.
    /// </summary>
    private static bool IsHeading(string paragraph)
    {
        return paragraph.StartsWith('#') && !paragraph.Contains('\n');
    }

    [GeneratedRegex(@"\r?\n[^\S\r\n]*\r?\n")]
    private static partial Regex BlankLineRegex();
}