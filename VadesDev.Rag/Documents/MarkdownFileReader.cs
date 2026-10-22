using VadesDev.Rag.Models;

namespace VadesDev.Rag.Documents;

/// <summary>
/// Reads one Markdown file and returns its raw, unchanged text.
/// </summary>
public class MarkdownFileReader
{
    /// <summary>
    /// Reads the file at the given path and returns its file name and text.
    /// </summary>
    public async Task<SourceDocument> ReadAsync(string path)
    {
        var text = await File.ReadAllTextAsync(path);

        return new SourceDocument(Path.GetFileName(path), text);
    }
}