using System.Text.RegularExpressions;

namespace VadesDev.Rag.Documents;

/// <summary>
/// Cleans a text before indexing: removes links, Markdown symbols, control characters and repeated spaces.
/// </summary>
public static partial class TextSanitizer
{
    /// <summary>
    /// Applies all cleaning rules in order and returns the cleaned text.
    /// </summary>
    public static string Sanitize(string text)
    {
        var withoutLinks = RemoveLinks(text);
        var withoutSymbols = RemoveMarkdownSymbols(withoutLinks);
        var withoutDangerousCharacters = RemoveDangerousCharacters(withoutSymbols);

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
    /// Removes Markdown symbols but keeps the words: heading markers (#), bold/italic markers (** and __) and backticks.
    /// </summary>
    private static string RemoveMarkdownSymbols(string text)
    {
        var withoutHeadingMarkers = HeadingMarkerRegex().Replace(text, "");

        return EmphasisMarkerRegex().Replace(withoutHeadingMarkers, "");
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

    [GeneratedRegex(@"^#{1,6}[^\S\r\n]*", RegexOptions.Multiline)]
    private static partial Regex HeadingMarkerRegex();

    [GeneratedRegex(@"\*\*|__|`")]
    private static partial Regex EmphasisMarkerRegex();

    [GeneratedRegex(@"[^\S\r\n]+")]
    private static partial Regex RepeatedSpacesRegex();
}