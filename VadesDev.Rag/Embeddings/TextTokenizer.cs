using System.Text.RegularExpressions;

namespace VadesDev.Rag.Embeddings;

/// <summary>
/// Splits a text into lowercase words, removes stop words and reduces plurals (days -> day).
/// </summary>
public static partial class TextTokenizer
{
    private static readonly HashSet<string> StopWords =
    [
        "the","is","are","a","an","of","to","on","in","your","our","you","do","does","what",
        "how","can","i","from","and","or","for","it","we","us","using",
        "who","when","where","why","which","long","much","many"
    ];

    /// <summary>
    /// Returns the normalized words of a text, without stop words.
    /// </summary>
    public static List<string> Tokenize(string text)
    {
        var words = new List<string>();

        foreach (Match match in WordRegex().Matches(text.ToLowerInvariant()))
        {
            if (!StopWords.Contains(match.Value))
            {
                words.Add(Normalize(match.Value));
            }
        }

        return words;
    }

    /// <summary>
    /// Crude plural stemmer: It removes a trailing s from words longer than 3 characters, so hours becomes hour while is and bus stay unchanged.
    /// </summary>
    private static string Normalize(string word)
    {
        return word.Length > 3 && word.EndsWith('s') ? word[..^1] : word;
    }

    /// <summary>
    /// Compile-time generated regex: finds lowercase words and digits.
    /// </summary>
    [GeneratedRegex("[a-z0-9]+")]
    private static partial Regex WordRegex();
}