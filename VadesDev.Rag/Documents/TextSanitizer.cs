using System.Text.RegularExpressions;

namespace VadesDev.Rag.Documents;

/// <summary>
/// Cleans a text before chunking: removes links, control characters and repeated spaces.
/// </summary>
public static partial class TextSanitizer
{
    /// <summary>
    /// Applies all cleaning rules in order and returns the cleaned text.
    /// </summary>
    public static string Sanitize(string text)
    {
        var withoutLinks = RemoveLinks(text);
        var withoutDangerousCharacters = RemoveDangerousCharacters(withoutLinks);

        return CollapseWhitespace(withoutDangerousCharacters);
    }

    /// <summary>
    /// Replaces Markdown links [text](url) with just their text, and removes raw URLs.
    /// </summary>
    private static string RemoveLinks(string text)
    {
        var withoutMarkdownLinks = MarkdownLinkRegex().Replace(text, "$1");

        return RawUrlRegex().Replace(withoutMarkdownLinks, "");
    }

    /// <summary>
    /// Removes control characters (except new lines and tabs) and zero-width characters.
    /// </summary>
    private static string RemoveDangerousCharacters(string text)
    {
        var cleaned = text.Where(c => !char.IsControl(c) || c is '\n' or '\r' or '\t')
                          .Where(c => c is not ('\u200B' or '\u200C' or '\u200D' or '\uFEFF'));

        return new string(cleaned.ToArray());
    }

    /// <summary>
    /// Collapses repeated spaces and tabs into one space and trims each line.
    /// </summary>
    private static string CollapseWhitespace(string text)
    {
        var collapsed = RepeatedSpacesRegex().Replace(text, " ");
        var lines = collapsed.Split('\n').Select(line => line.Trim());

        return string.Join("\n", lines);
    }

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex RawUrlRegex();

    [GeneratedRegex(@"[^\S\r\n]+")]
    private static partial Regex RepeatedSpacesRegex();
}